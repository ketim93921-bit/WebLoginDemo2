using System.ComponentModel.DataAnnotations;

namespace WebLoginDemo2.Models   // ✅ 這裡要跟 AuthController 的 using WebLoginDemo2.Models 對得起來
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "請輸入帳號")]
        [Display(Name = "帳號")]
        public string UserName { get; set; } = null!;

        [Required(ErrorMessage = "請輸入密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = null!;

    }
}
