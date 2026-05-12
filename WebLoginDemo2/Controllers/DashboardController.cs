using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebLoginDemo2.Controllers
{
    /// <summary>
    /// 登入後才能看到的 Dashboard 主頁
    /// 
    /// 加上 [Authorize] 表示：
    ///   - 如果沒有登入（沒有 Cookie 身分）
    ///     -> 自動被導向 Auth/Login
    /// 
    /// Cookie 驗證流程已由 Program.cs 設定完成
    /// </summary>
    [Authorize]
    public class DashboardController : Controller
    {
        /// <summary>
        /// 主控制台的主頁
        /// 未來可以在這裡顯示感測資訊
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }
    }
}
