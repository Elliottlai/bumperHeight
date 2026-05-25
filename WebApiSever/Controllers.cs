using JsonType;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;

namespace WebApiSever
{
    [ApiController]
    [Route("api/[controller]")]
    public class CIMapiController : ControllerBase
    {
        public static event Action<string>? OnRequestReceived;

        private static void Log(string message)
            => OnRequestReceived?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        // ====================== QuerySerialNoData ======================
        [HttpPost("QuerySerialNoData")]
        public IActionResult QuerySerialNoData([FromBody] QuerySerialNoDataRequest request)
        {
            Log($"[QuerySerialNoData] EquipmentId={request.EquipmentId}, ProductId={request.ProductId}");

            var response = new QuerySerialNoDataResponse
            {
                EventType = request.EventType,
                RecordId = request.RecordId,
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = request.EquipmentId,
                ProductId = request.ProductId,
                QueryData = new QueryData
                {
                    MaterialNo = "MAT-001",
                    WorkerOrderNo = "WO-20260430",
                    LotNo = "LOT-001",
                    RfidLotNo = "RFID-001",
                    RecipeId = "RECIPE-001"
                },
                ReturnMessage = new ReturnMessage { Code = "0", Message = "Success" }
            };

            Log($"[QuerySerialNoData] Response => Code={response.ReturnMessage.Code}");
            return Ok(response);
        }

        // ====================== EqStatusReport ======================
        [HttpPost("EqStatusReport")]
        public IActionResult EqStatusReport([FromBody] EqStatusReportRequest request)
        {
            Log($"[EqStatusReport] EquipmentId={request.EquipmentId}, Status={request.EquipmentStatus}");

            var response = new EqStatusReportResponse
            {
                EventType = request.EventType,
                RecordId = request.RecordId,
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = request.EquipmentId,
                EquipmentStatus = request.EquipmentStatus,
                ReturnMessage = new ReturnMessage { Code = "0", Message = "Success" }
            };

            Log($"[EqStatusReport] Response => Code={response.ReturnMessage.Code}");
            return Ok(response);
        }

        // ====================== MeasureResultUpload ======================
        [HttpPost("MeasureResultUpload")]
        public IActionResult MeasureResultUpload([FromBody] MeasureResultUploadRequest request)
        {
            Log($"[MeasureResultUpload] EquipmentId={request.EquipmentId}, ProductId={request.ProductId}, TotalJudge={request.TotalJudge}, Items={request.MeasureResultList?.Count}");

            var response = new MeasureResultUploadResponse
            {
                EventType = request.EventType,
                RecordId = request.RecordId,
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = request.EquipmentId,
                ProductId = request.ProductId,
                RecipeId = request.RecipeId,
                TotalJudge = request.TotalJudge,
                ProductType = request.ProductType,
                ProcessStartTime = request.ProcessStartTime,
                ProcessEndTime = request.ProcessEndTime,
                MeasureResultList = request.MeasureResultList,
                ReturnMessage = new ReturnMessage { Code = "0", Message = "Success" }
            };

            Log($"[MeasureResultUpload] Response => Code={response.ReturnMessage.Code}");
            return Ok(response);
        }
    }
}