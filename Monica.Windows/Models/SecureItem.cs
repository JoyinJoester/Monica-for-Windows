using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Monica.Windows.Models
{
    public enum ItemType
    {
        Password = 0, // 兼容
        Totp = 1,
        BankCard = 2,
        Document = 3,
        Note = 4
    }

    [Table("SecureItems")]
    public class SecureItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public ItemType ItemType { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        // Encrypted JSON data containing type-specific fields
        public string ItemData { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public bool IsFavorite { get; set; }
        public int SortOrder { get; set; }
    }
}
