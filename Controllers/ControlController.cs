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

        // ==============================
        // 舊版風扇控制 API
        // 保留給 Dashboard / LINE 舊程式使用
        // 目前將風扇對應為 Relay1
        // ==============================
        [HttpPost("fan/on")]
        public async Task<IActionResult> FanOn()
        {
            return await SetRelayAsync(1, true, "風扇");
        }

        [HttpPost("fan/off")]
        public async Task<IActionResult> FanOff()
        {
            return await SetRelayAsync(1, false, "風扇");
        }

        // ==============================
        // 新版 Relay 控制 API
        // POST /api/control/relay/1/on
        // POST /api/control/relay/1/off
        // Relay 編號：1 ~ 6
        // ==============================
        [HttpPost("relay/{relayNumber:int}/on")]
        public async Task<IActionResult> RelayOn(int relayNumber)
        {
            return await SetRelayAsync(relayNumber, true, $"Relay{relayNumber}");
        }

        [HttpPost("relay/{relayNumber:int}/off")]
        public async Task<IActionResult> RelayOff(int relayNumber)
        {
            return await SetRelayAsync(relayNumber, false, $"Relay{relayNumber}");
        }

        // ==============================
        // Stepper 步進馬達控制 API
        // POST /api/control/stepper/on
        // POST /api/control/stepper/off
        // ==============================
        [HttpPost("stepper/on")]
        public async Task<IActionResult> StepperOn()
        {
            return await SetStepperAsync(true);
        }

        [HttpPost("stepper/off")]
        public async Task<IActionResult> StepperOff()
        {
            return await SetStepperAsync(false);
        }

        private async Task<IActionResult> SetRelayAsync(
            int relayNumber,
            bool on,
            string deviceName)
        {
            if (relayNumber < 1 || relayNumber > 6)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Relay 編號錯誤，必須是 1 到 6。"
                });
            }

            try
            {
                await _mqttService.PublishRelayCommandAsync(relayNumber, on);

                return Ok(new
                {
                    success = true,
                    device = deviceName,
                    relay = relayNumber,
                    state = on ? "ON" : "OFF",
                    message = $"{deviceName} 已{(on ? "開啟" : "關閉")}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = deviceName,
                    relay = relayNumber,
                    message = $"{deviceName} 控制失敗",
                    detail = ex.Message
                });
            }
        }

        private async Task<IActionResult> SetStepperAsync(bool on)
        {
            try
            {
                await _mqttService.PublishStepperCommandAsync(on);

                return Ok(new
                {
                    success = true,
                    device = "stepper",
                    state = on ? "ON" : "OFF",
                    message = on ? "步進馬達已啟動" : "步進馬達已關閉"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "stepper",
                    message = "步進馬達控制失敗",
                    detail = ex.Message
                });
            }
        }
    }
}