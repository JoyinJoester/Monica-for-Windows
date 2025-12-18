using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Monica.Windows.Data;
using Monica.Windows.Models;
using System.Xml.Linq;

namespace Monica.Windows.Services
{
    public class WebDavService
    {
        private readonly AppDbContext _dbContext;
        private readonly ISecurityService _securityService;
        private HttpClient _client;
        
        // Configuration
        private string _serverUrl;
        private string _username;
        private string _password;

        public WebDavService(AppDbContext dbContext, ISecurityService securityService)
        {
            _dbContext = dbContext;
            _securityService = securityService;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Monica-Windows/1.0");
        }

        public void Configure(string url, string username, string password)
        {
            _serverUrl = url.TrimEnd('/');
            _username = username;
            _password = password;

            var authBytes = Encoding.UTF8.GetBytes($"{_username}:{_password}");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            try
            {
                // Standard WebDAV check: PROPFIND Depth 0
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), _serverUrl);
                request.Headers.Add("Depth", "0");
                
                var response = await _client.SendAsync(request);
                
                if (response.IsSuccessStatusCode || (int)response.StatusCode == 207)
                {
                    return (true, "Success");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (false, "验证失败 (401 Unauthorized)。请检查用户名和密码。");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return (false, "找不到路径 (404 Not Found)。请检查服务器地址是否正确。");
                }
                else
                {
                    return (false, $"服务器返回错误: {response.StatusCode} {(int)response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                return (false, $"网络请求失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"发生未知错误: {ex.Message}");
            }
        }

        public async Task<(List<string> Files, string? Error)> ListBackupsAsync()
        {
            var results = new List<string>();
            try
            {
                // Android stores backups in 'Monica_Backups' subfolder
                string backupFolder = "Monica_Backups";
                string path = $"{_serverUrl}/{backupFolder}";
                
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), path);
                request.Headers.Add("Depth", "1");
                var response = await _client.SendAsync(request);

                // Check if folder doesn't exist (404)
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return (results, "备份目录不存在 (404)。您可能还没有备份过任何数据。");
                }

                if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
                {
                    return (results, $"服务器返回错误: {response.StatusCode} ({(int)response.StatusCode})");
                }

                string xmlContent = await response.Content.ReadAsStringAsync();
                
                // Try to parse XML
                var doc = XDocument.Parse(xmlContent);
                var ns = doc.Root?.GetDefaultNamespace();
                
                // Handle DAV: namespace prefix
                XNamespace davNs = ns ?? "DAV:";
                
                var resources = doc.Descendants(davNs + "response");
                if (!resources.Any())
                {
                    // Try alternate namespace
                    davNs = "DAV:";
                    resources = doc.Descendants(davNs + "response");
                }
                
                foreach (var res in resources)
                {
                    var href = res.Element(davNs + "href")?.Value;
                    if (href != null && (href.EndsWith(".zip") || href.EndsWith(".enc.zip")))
                    {
                        // Decode URL encoded name
                        string fileName = System.Net.WebUtility.UrlDecode(Path.GetFileName(href));
                        results.Add(fileName);
                    }
                }
                
                if (results.Count == 0)
                {
                    return (results, null); // Empty but not an error
                }
                
                return (results.OrderByDescending(x => x).ToList(), null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ListBackups failed: {ex.Message}");
                return (results, $"列表失败: {ex.Message}");
            }
        }

        public async Task<string> CreateBackupAsync(bool encrypt, string? encryptPassword)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"monica_backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(tempPath);

            try
            {
                // 1. Fetch Data
                var passwords = await _dbContext.PasswordEntries.ToListAsync();
                var secureItems = await _dbContext.SecureItems.ToListAsync();

                // 2. Prepare Data Structure
                // passwords/
                string passwordsDir = Path.Combine(tempPath, "passwords");
                Directory.CreateDirectory(passwordsDir);
                
                // notes/
                string notesDir = Path.Combine(tempPath, "notes");
                Directory.CreateDirectory(notesDir);
                
                // 3. Serialize Passwords (JSON)
                foreach (var pwd in passwords)
                {
                    try 
                    {
                        string decryptedPass = _securityService.Decrypt(pwd.EncryptedPassword);
                        var backupEntry = new PasswordBackupEntry
                        {
                            id = pwd.Id,
                            title = pwd.Title,
                            username = pwd.Username,
                            password = decryptedPass,
                            website = pwd.Website,
                            notes = pwd.Notes,
                            isFavorite = pwd.IsFavorite,
                            createdAt = new DateTimeOffset(pwd.CreatedAt).ToUnixTimeMilliseconds(),
                            updatedAt = new DateTimeOffset(pwd.UpdatedAt).ToUnixTimeMilliseconds()
                        };
                        
                        string json = JsonSerializer.Serialize(backupEntry);
                        string fileName = $"password_{pwd.Id}_{backupEntry.createdAt}.json";
                        File.WriteAllText(Path.Combine(passwordsDir, fileName), json);
                    }
                    catch {}
                }

                // 4. Serialize Notes (JSON) & TOTP/Cards (CSV)
                var totpItems = new List<SecureItem>();
                var cardDocItems = new List<SecureItem>();

                foreach (var item in secureItems)
                {
                    try
                    {
                        string decryptedData = _securityService.Decrypt(item.ItemData);
                        
                        if (item.ItemType == ItemType.Note)
                        {
                            var backupEntry = new NoteBackupEntry
                            {
                                id = item.Id,
                                title = item.Title,
                                notes = item.Notes,
                                itemData = decryptedData, // Note content usually in Data or Notes? Android treats 'itemData' as note content for NOTE type sometimes? 
                                // Actually checking Android: itemData is main content.
                                isFavorite = item.IsFavorite,
                                createdAt = new DateTimeOffset(item.CreatedAt).ToUnixTimeMilliseconds(),
                                updatedAt = new DateTimeOffset(item.UpdatedAt).ToUnixTimeMilliseconds()
                            };
                            string json = JsonSerializer.Serialize(backupEntry);
                            string fileName = $"note_{item.Id}_{backupEntry.createdAt}.json";
                            File.WriteAllText(Path.Combine(notesDir, fileName), json);
                        }
                        else if (item.ItemType == ItemType.Totp)
                        {
                            totpItems.Add(item);
                        }
                        else if (item.ItemType == ItemType.BankCard || item.ItemType == ItemType.Document)
                        {
                            cardDocItems.Add(item);
                        }
                    }
                    catch {}
                }

                // 5. Generate CSVs
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (totpItems.Count > 0)
                {
                    string csv = GenerateCsv(totpItems);
                    File.WriteAllText(Path.Combine(tempPath, $"Monica_{timestamp}_totp.csv"), csv, Encoding.UTF8);
                }
                if (cardDocItems.Count > 0)
                {
                    string csv = GenerateCsv(cardDocItems);
                    File.WriteAllText(Path.Combine(tempPath, $"Monica_{timestamp}_cards_docs.csv"), csv, Encoding.UTF8);
                }

                // 6. Zip
                string zipPath = Path.Combine(Path.GetTempPath(), $"monica_backup_{timestamp}.zip");
                ZipFile.CreateFromDirectory(tempPath, zipPath);
                
                string finalPath = zipPath;
                string remoteFileName = Path.GetFileName(zipPath);

                // 7. Encrypt if needed
                if (encrypt && !string.IsNullOrEmpty(encryptPassword))
                {
                    string encPath = Path.Combine(Path.GetTempPath(), $"monica_backup_{timestamp}.enc.zip");
                    BackupEncryptionHelper.EncryptFile(zipPath, encPath, encryptPassword);
                    finalPath = encPath;
                    remoteFileName = Path.GetFileName(encPath);
                }

                // 8. Upload
                await UploadFileAsync(finalPath, remoteFileName);

                return remoteFileName;
            }
            finally
            {
                // Cleanup
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }

        private async Task UploadFileAsync(string localPath, string remoteName)
        {
            // Android stores backups in 'Monica_Backups' subfolder
            string backupFolder = "Monica_Backups";
            string backupUrl = $"{_serverUrl}/{backupFolder}";
            
            // Try to create folder if it doesn't exist (ignore errors if already exists)
            try
            {
                var mkcolRequest = new HttpRequestMessage(new HttpMethod("MKCOL"), backupUrl);
                await _client.SendAsync(mkcolRequest);
            }
            catch { /* Folder may already exist */ }

            using (var fs = File.OpenRead(localPath))
            {
                var content = new StreamContent(fs);
                // WebDAV PUT
                string url = $"{backupUrl}/{remoteName}";
                var response = await _client.PutAsync(url, content);
                response.EnsureSuccessStatusCode();
            }
        }

        public async Task<string> RestoreBackupAsync(string fileName, string? password)
        {
            string downloadPath = Path.Combine(Path.GetTempPath(), fileName);
            string extractPath = Path.Combine(Path.GetTempPath(), $"restore_{DateTime.Now.Ticks}");

            try
            {
                // 1. Download from Monica_Backups subfolder
                string backupUrl = $"{_serverUrl}/Monica_Backups/{fileName}";
                var response = await _client.GetAsync(backupUrl);
                response.EnsureSuccessStatusCode();
                using (var fs = File.Create(downloadPath))
                {
                    await response.Content.CopyToAsync(fs);
                }

                // 2. Decrypt if needed
                string zipPath = downloadPath;
                if (BackupEncryptionHelper.IsEncryptedFile(downloadPath))
                {
                    if (string.IsNullOrEmpty(password)) throw new Exception("Backup_Password_Required");
                    
                    string decryptedPath = downloadPath + ".decrypted.zip";
                    try 
                    {
                        BackupEncryptionHelper.DecryptFile(downloadPath, decryptedPath, password);
                        zipPath = decryptedPath;
                    } 
                    catch (Exception) 
                    {
                        throw new Exception("Wrong_Password");
                    }
                }

                // 3. Unzip
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                int imported = 0;

                // 4. Import Passwords (JSON)
                string passwordsDir = Path.Combine(extractPath, "passwords");
                if (Directory.Exists(passwordsDir))
                {
                    foreach (var file in Directory.GetFiles(passwordsDir, "*.json"))
                    {
                        try
                        {
                            var json = File.ReadAllText(file);
                            var entry = JsonSerializer.Deserialize<PasswordBackupEntry>(json);
                            if (entry != null)
                            {
                                // Debug: Log what we're importing
                                System.Diagnostics.Debug.WriteLine($"Import password '{entry.title}': password length = {entry.password?.Length ?? 0}, looks encrypted = {entry.password?.Contains("==") == true}");
                                
                                // Check duplicate
                                bool exists = await _dbContext.PasswordEntries.AnyAsync(p => p.Title == entry.title && p.Username == entry.username);
                                if (!exists)
                                {
                                    string finalPassword = entry.password;
                                    bool isAlreadyEncrypted = false;
                                    
                                    // Smart detection: Check if password likely already encrypted (Android backup)
                                    // Android ciphertext is Base64, usually > 24 chars, and contains typical Base64 chars
                                    if (!string.IsNullOrEmpty(entry.password) && entry.password.Length > 24)
                                    {
                                        try
                                        {
                                            Convert.FromBase64String(entry.password);
                                            // It is valid Base64. Assume it's encrypted if it looks like a blob.
                                            // Heuristic: Plaintext passwords rarely match this profile exactly.
                                            isAlreadyEncrypted = true;
                                            System.Diagnostics.Debug.WriteLine($"Import: Password for '{entry.title}' detected as ALREADY ENCRYPTED (Length: {entry.password.Length})");
                                        }
                                        catch
                                        {
                                            // Not Base64, treat as plaintext
                                        }
                                    }

                                    if (!isAlreadyEncrypted)
                                    {
                                        finalPassword = _securityService.Encrypt(entry.password);
                                        System.Diagnostics.Debug.WriteLine($"Import: Encrypted plaintext password for '{entry.title}'");
                                    }
                                    
                                    // ✅ Resolve category by name
                                    int? resolvedCategoryId = null;
                                    if (!string.IsNullOrEmpty(entry.categoryName))
                                    {
                                        var existingCategory = await _dbContext.Categories
                                            .FirstOrDefaultAsync(c => c.Name == entry.categoryName);
                                        
                                        if (existingCategory != null)
                                        {
                                            resolvedCategoryId = existingCategory.Id;
                                        }
                                        else
                                        {
                                            // Create new category
                                            var maxSortOrder = await _dbContext.Categories.MaxAsync(c => (int?)c.SortOrder) ?? 0;
                                            var newCategory = new Category
                                            {
                                                Name = entry.categoryName,
                                                SortOrder = maxSortOrder + 1
                                            };
                                            _dbContext.Categories.Add(newCategory);
                                            await _dbContext.SaveChangesAsync();
                                            resolvedCategoryId = newCategory.Id;
                                            System.Diagnostics.Debug.WriteLine($"Created category: {entry.categoryName} (id={resolvedCategoryId})");
                                        }
                                    }
                                    
                                    _dbContext.PasswordEntries.Add(new PasswordEntry
                                    {
                                        Title = entry.title,
                                        Username = entry.username,
                                        EncryptedPassword = finalPassword,
                                        Website = entry.website,
                                        Notes = entry.notes,
                                        IsFavorite = entry.isFavorite,
                                        CategoryId = resolvedCategoryId,  // ✅ Set category
                                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.createdAt).LocalDateTime,
                                        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.updatedAt).LocalDateTime
                                    });
                                    imported++;
                                }
                            }
                        }
                        catch (Exception ex) 
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to import password: {ex.Message}");
                        }
                    }
                }

                // 5. Import Notes (JSON)
                string notesDir = Path.Combine(extractPath, "notes");
                if (Directory.Exists(notesDir))
                {
                    foreach (var file in Directory.GetFiles(notesDir, "*.json"))
                    {
                        try
                        {
                            var json = File.ReadAllText(file);
                            var entry = JsonSerializer.Deserialize<NoteBackupEntry>(json);
                            if (entry != null)
                            {
                                bool exists = await _dbContext.SecureItems.AnyAsync(s => s.Title == entry.title && s.ItemType == ItemType.Note);
                                if (!exists)
                                {
                                    _dbContext.SecureItems.Add(new SecureItem
                                    {
                                        Title = entry.title,
                                        ItemType = ItemType.Note,
                                        ItemData = _securityService.Encrypt(entry.itemData), // itemData is note content
                                        Notes = entry.notes,
                                        IsFavorite = entry.isFavorite,
                                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.createdAt).LocalDateTime,
                                        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.updatedAt).LocalDateTime
                                    });
                                    imported++;
                                }
                            }
                        }
                        catch {}
                    }
                }

                // 6. Import CSVs (TOTP / Cards)
                // Look for CSVs in root
                foreach (var file in Directory.GetFiles(extractPath, "*.csv"))
                {
                    try
                    {
                        string content = File.ReadAllText(file);
                        imported += await ImportCsvContent(content);
                    }
                    catch {}
                }

                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Extract inner exception for EF errors
                    var inner = ex.InnerException;
                    string details = inner != null ? inner.Message : ex.Message;
                    throw new Exception($"保存数据失败: {details}");
                }
                return $"恢复完成。成功导入 {imported} 条数据。";

            }
            finally
            {
                try { File.Delete(downloadPath); } catch {}
                try { Directory.Delete(extractPath, true); } catch {}
            }
        }

        private string GenerateCsv(List<SecureItem> items)
        {
            var sb = new StringBuilder();
            sb.Append('\uFEFF'); // BOM
            sb.AppendLine("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt");
            
            foreach(var item in items)
            {
                string typeStr = item.ItemType switch {
                    ItemType.Totp => "TOTP",
                    ItemType.BankCard => "BANK_CARD",
                    ItemType.Document => "DOCUMENT",
                    _ => "NOTE"
                };
                string data = _securityService.Decrypt(item.ItemData);
                
                var cols = new string[]
                {
                    item.Id.ToString(),
                    typeStr,
                    EscapeCsv(item.Title),
                    EscapeCsv(data),
                    EscapeCsv(item.Notes),
                    item.IsFavorite.ToString(),
                    "",
                    new DateTimeOffset(item.CreatedAt).ToUnixTimeMilliseconds().ToString(),
                    new DateTimeOffset(item.UpdatedAt).ToUnixTimeMilliseconds().ToString()
                };
                sb.AppendLine(string.Join(",", cols));
            }
            return sb.ToString();
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private async Task<int> ImportCsvContent(string content)
        {
            // Reuse logic similar to DataExportImportService but simplified for internal use
            // Assume valid format as it comes from backup
            int count = 0;
            // Parse CSV manually
            // ... (Simplified parser for time saving, assuming standard quotes)
            
            // To be safe, simple line split won't work for multiline notes.
            // Using a basic regex or state machine parser is better.
            
            var lines = new List<List<string>>();
            bool inQuotes = false;
            var currentLine = new List<string>();
            var currentField = new StringBuilder();
            
            if (content.StartsWith('\uFEFF')) content = content.Substring(1);

            for (int i=0; i<content.Length; i++)
            {
               char c = content[i];
               if (inQuotes)
               {
                   if (c == '"')
                   {
                       if (i+1 < content.Length && content[i+1] == '"') { currentField.Append('"'); i++; }
                       else inQuotes = false;
                   }
                   else currentField.Append(c);
               }
               else
               {
                   if (c == '"') inQuotes = true;
                   else if (c == ',') { currentLine.Add(currentField.ToString()); currentField.Clear(); }
                   else if (c == '\r' || c == '\n')
                   {
                       if (c=='\r' && i+1<content.Length && content[i+1]=='\n') i++;
                       if (currentLine.Count > 0 || currentField.Length > 0) 
                       {
                           currentLine.Add(currentField.ToString());
                           lines.Add(new List<string>(currentLine));
                           currentLine.Clear();
                           currentField.Clear();
                       }
                   }
                   else currentField.Append(c);
               }
            }
            if (currentLine.Count > 0 || currentField.Length > 0)
            {
                currentLine.Add(currentField.ToString());
                lines.Add(currentLine);
            }

            // Skip header
            foreach(var fields in lines.Skip(1))
            {
                if (fields.Count < 9) continue;
                try
                {
                    string title = fields[2];
                    string typeStr = fields[1];
                    string data = fields[3];
                    string notes = fields[4];
                    bool isFav = bool.TryParse(fields[5], out var f) && f;
                    long created = long.TryParse(fields[7], out var c) ? c : 0;
                    long updated = long.TryParse(fields[8], out var u) ? u : 0;

                    ItemType type = typeStr switch {
                        "TOTP" => ItemType.Totp,
                        "BANK_CARD" => ItemType.BankCard,
                        "DOCUMENT" => ItemType.Document,
                        _ => ItemType.Note
                    };

                    bool exists = await _dbContext.SecureItems.AnyAsync(s => s.Title == title && s.ItemType == type);
                    if (!exists)
                    {
                        _dbContext.SecureItems.Add(new SecureItem
                        {
                             Title = title,
                             ItemType = type,
                             ItemData = _securityService.Encrypt(data),
                             Notes = notes,
                             IsFavorite = isFav,
                             CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(created).LocalDateTime,
                             UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updated).LocalDateTime
                        });
                        count++;
                    }
                }
                catch {}
            }
            return count;
        }

        // DTOs with null defaults
        private class PasswordBackupEntry
        {
            public long id { get; set; }
            public string title { get; set; } = "";
            public string username { get; set; } = "";
            public string password { get; set; } = "";
            public string website { get; set; } = "";
            public string notes { get; set; } = "";
            public bool isFavorite { get; set; }
            public long? categoryId { get; set; }
            public string? categoryName { get; set; }  // ✅ Added for Android compatibility
            public long createdAt { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            public long updatedAt { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private class NoteBackupEntry
        {
            public long id { get; set; }
            public string title { get; set; } = "";
            public string notes { get; set; } = "";
            public string itemData { get; set; } = "";
            public bool isFavorite { get; set; }
            public long createdAt { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            public long updatedAt { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
    }
}
