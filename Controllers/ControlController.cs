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

        // 舊版 API 保留，內部對應生長燈 / Relay6
        [HttpPost("fan/on")]
        public async Task<IActionResult> FanOn()
        {
            return await SetRelayAsync(6, true, "生長燈");
        }

        [HttpPost("fan/off")]
        public async Task<IActionResult> FanOff()
        {
            return await SetRelayAsync(6, false, "生長燈");
        }

        [HttpPost("relay/{relayNumber:int}/on")]
        public async Task<IActionResult> RelayOn(int relayNumber)
        {
            return await SetRelayAsync(relayNumber, true, "生長燈");
        }

        [HttpPost("relay/{relayNumber:int}/off")]
        public async Task<IActionResult> RelayOff(int relayNumber)
        {
            return await SetRelayAsync(relayNumber, false, "生長燈");
        }

        [HttpPost("relay6/timer/{unitCount:int}")]
        public async Task<IActionResult> Relay6Timer(int unitCount)
        {
            if (unitCount < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "生長燈定時單位不能小於 0。"
                });
            }

            if (unitCount > 144)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "生長燈定時最多 144 單位，也就是 24 小時。"
                });
            }

            try
            {
                await _mqttService.PublishRelay6TimerCommandAsync(unitCount);

                int minutes = unitCount * 10;

                return Ok(new
                {
                    success = true,
                    device = "生長燈",
                    timerUnit = unitCount,
                    minutes,
                    state = unitCount > 0 ? "ON" : "OFF",
                    message = unitCount > 0
                        ? $"生長燈已啟動定時 {minutes} 分鐘"
                        : "生長燈定時已取消並關閉"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "生長燈",
                    message = "生長燈定時控制失敗",
                    detail = ex.Message
                });
            }
        }

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

        [HttpPost("stepper/timer/{unitCount:int}")]
        public async Task<IActionResult> StepperTimer(int unitCount)
        {
            if (unitCount < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "液肥定時單位不能小於 0。"
                });
            }

            if (unitCount > 144)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "液肥定時最多 144 單位，也就是 24 小時。"
                });
            }

            try
            {
                await _mqttService.PublishStepperTimerCommandAsync(unitCount);

                int minutes = unitCount * 10;

                return Ok(new
                {
                    success = true,
                    device = "液肥",
                    timerUnit = unitCount,
                    minutes,
                    state = unitCount > 0 ? "ON" : "OFF",
                    message = unitCount > 0
                        ? $"液肥已啟動定時 {minutes} 分鐘"
                        : "液肥定時已取消並停止"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "液肥",
                    message = "液肥定時控制失敗",
                    detail = ex.Message
                });
            }
        }

        private async Task<IActionResult> SetRelayAsync(
            int relayNumber,
            bool on,
            string deviceName)
        {
            if (relayNumber != 6)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "目前新版 Arduino 只有生長燈支援遠端控制。"
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
                    message = $"{deviceName}已{(on ? "開啟" : "關閉")}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = deviceName,
                    relay = relayNumber,
                    message = $"{deviceName}控制失敗",
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
                    device = "液肥",
                    state = on ? "ON" : "OFF",
                    message = on ? "液肥已啟動" : "液肥已關閉"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "液肥",
                    message = "液肥控制失敗",
                    detail = ex.Message
                });
            }
        }
    }
}