using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebLoginDemo2.Services;

namespace WebLoginDemo2.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/control")]
    public class ControlController : ControllerBase
    {
        private readonly MqttService _mqttService;

        public ControlController(MqttService mqttService)
        {
            _mqttService = mqttService;
        }

        [HttpPost("fan/on")]
        public async Task<IActionResult> FanOn()
        {
            return await SetFanAsync(true);
        }

        [HttpPost("fan/off")]
        public async Task<IActionResult> FanOff()
        {
            return await SetFanAsync(false);
        }

        private async Task<IActionResult> SetFanAsync(bool on)
        {
            try
            {
                await _mqttService.PublishFanCommandAsync(on);

                return Ok(new
                {
                    success = true,
                    device = "fan",
                    state = on ? "ON" : "OFF",
                    message = on ? "風扇已開啟" : "風扇已關閉"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "風扇控制失敗",
                    detail = ex.Message
                });
            }
        }
    }
}