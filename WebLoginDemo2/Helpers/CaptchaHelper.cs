namespace WebLoginDemo2.Helpers
{
  
    /// 專門負責產生驗證碼字串的工具類別

    public static class CaptchaHelper
    {

        private static readonly char[] _chars =
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        
        /// 產生隨機驗證碼字串（預設 4 碼）
        /// <param name="length">驗證碼長度</param>
        public static string GenerateCode(int length = 4)
        {
            var rng = Random.Shared;
            var buffer = new char[length];

            for (int i = 0; i < length; i++)
            {
                buffer[i] = _chars[rng.Next(_chars.Length)];
            }

            return new string(buffer);
        }
    }
}

