using System;
using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models
{
    public class AppUser
    {
        [Key]
        public int Id { get; set; }
        public string UserName { get; set; } = null!;
        public string Email { get; set; } = string.Empty;
        // 儲存密碼雜湊值
        public string PasswordHash { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}