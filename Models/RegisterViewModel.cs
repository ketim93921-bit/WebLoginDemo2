using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.ViewModels
{
    public class RegisterViewModel
    {
        [Required]
        [Display(Name = "帳號")]
        public string UserName { get; set; } = null!;

        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "兩次密碼不一致")]
        [Display(Name = "確認密碼")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
