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
        // 保留給舊程式使用
        // 目前新版 Arduino 會對應到 Relay6 / D6
        // ==============================
        [HttpPost("fan/on")]
        public async Task<IActionResult> FanOn()
        {
            return await SetRelayAsync(6, true, "風扇 / Relay6");
        }

        [HttpPost("fan/off")]
        public async Task<IActionResult> FanOff()
        {
            return await SetRelayAsync(6, false, "風扇 / Relay6");
        }

        // ==============================
        // Relay 控制 API
        // 新版 Arduino 目前只有 Relay6 / D6 支援遠端控制
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
        // Relay6 定時控制 API
        // 新版 Arduino：1 單位 = 10 分鐘
        //
        // POST /api/control/relay6/timer/1  = 10 分鐘
        // POST /api/control/relay6/timer/2  = 20 分鐘
        // POST /api/control/relay6/timer/3  = 30 分鐘
        // POST /api/control/relay6/timer/0  = 取消定時並關閉
        // ==============================
        [HttpPost("relay6/timer/{unitCount:int}")]
        public async Task<IActionResult> Relay6Timer(int unitCount)
        {
            if (unitCount < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Relay6 定時單位不能小於 0。"
                });
            }

            if (unitCount > 144)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Relay6 定時最多 144 單位，也就是 24 小時。"
                });
            }

            try
            {
                await _mqttService.PublishRelay6TimerCommandAsync(unitCount);

                int minutes = unitCount * 10;

                return Ok(new
                {
                    success = true,
                    device = "Relay6",
                    timerUnit = unitCount,
                    minutes,
                    state = unitCount > 0 ? "ON" : "OFF",
                    message = unitCount > 0
                        ? $"Relay6 已啟動定時 {minutes} 分鐘"
                        : "Relay6 定時已取消並關閉"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "Relay6",
                    message = "Relay6 定時控制失敗",
                    detail = ex.Message
                });
            }
        }

        // ==============================
        // Stepper 步進馬達控制 API
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

        // ==============================
        // Stepper 定時控制 API
        // 新版 Arduino：1 單位 = 10 分鐘
        //
        // POST /api/control/stepper/timer/1 = 10 分鐘
        // POST /api/control/stepper/timer/2 = 20 分鐘
        // POST /api/control/stepper/timer/0 = 取消定時並停止
        // ==============================
        [HttpPost("stepper/timer/{unitCount:int}")]
        public async Task<IActionResult> StepperTimer(int unitCount)
        {
            if (unitCount < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Stepper 定時單位不能小於 0。"
                });
            }

            if (unitCount > 144)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Stepper 定時最多 144 單位，也就是 24 小時。"
                });
            }

            try
            {
                await _mqttService.PublishStepperTimerCommandAsync(unitCount);

                int minutes = unitCount * 10;

                return Ok(new
                {
                    success = true,
                    device = "stepper",
                    timerUnit = unitCount,
                    minutes,
                    state = unitCount > 0 ? "ON" : "OFF",
                    message = unitCount > 0
                        ? $"步進馬達已啟動定時 {minutes} 分鐘"
                        : "步進馬達定時已取消並停止"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    device = "stepper",
                    message = "步進馬達定時控制失敗",
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
                    message = "目前新版 Arduino 只有 Relay6 / D6 支援遠端控制。"
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