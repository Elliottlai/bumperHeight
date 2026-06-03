using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JsonType
{
    #region  ====================== QuerySerialNoData =========================
    // ====================== Request ======================
    public class QuerySerialNoDataRequest
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "QuerySerialNoData";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("ProductId")]
        public string ProductId { get; set; }

        public QuerySerialNoDataRequest()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }

    // ====================== Response ======================
    public class QuerySerialNoDataResponse
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; }

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; }

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("ProductId")]
        public string ProductId { get; set; }

        [JsonPropertyName("QueryData")]
        public QueryData QueryData { get; set; }

        [JsonPropertyName("ReturnMessage")]
        public ReturnMessage ReturnMessage { get; set; }
    }

    public class QueryData
    {
        [JsonPropertyName("MaterialNo")]
        public string MaterialNo { get; set; }

        [JsonPropertyName("WorkerOrderNo")]
        public string WorkerOrderNo { get; set; }

        [JsonPropertyName("LotNo")]
        public string LotNo { get; set; }

        [JsonPropertyName("RfidLotNo")]
        public string RfidLotNo { get; set; }

        [JsonPropertyName("RecipeId")]
        public string RecipeId { get; set; }
    }
    #endregion

    #region ====================== EqStatusReport ===========================
    // ====================== Request ======================
    public class EqStatusReportRequest
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "EqStatusReport";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("EquipmentStatus")]
        public string EquipmentStatus { get; set; }

        public EqStatusReportRequest()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
    // ====================== Response ======================
    public class EqStatusReportResponse
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "EqStatusReport";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("EquipmentStatus")]
        public string EquipmentStatus { get; set; }

        public EqStatusReportResponse()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
        public ReturnMessage ReturnMessage { get; set; }
    }
    #endregion

    #region ====================== MeasureResultUpload ======================
    public class MeasureResultUploadRequest
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "MeasureResultUpload";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("ProductId")]
        public string ProductId { get; set; }

        [JsonPropertyName("RecipeId")]
        public string RecipeId { get; set; }

        [JsonPropertyName("TotalJudge")]
        public string TotalJudge { get; set; } = "Pass";

        [JsonPropertyName("ProductType")]
        public string ProductType { get; set; } = "";

        [JsonPropertyName("ProcessStartTime")]
        public string ProcessStartTime { get; set; } = "";

        [JsonPropertyName("ProcessEndTime")]
        public string ProcessEndTime { get; set; } = "";

        [JsonPropertyName("MeasureResultList")]
        public List<MeasureResultItem> MeasureResultList { get; set; } = new List<MeasureResultItem>();

        public MeasureResultUploadRequest()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
    public class MeasureResultData
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "MeasureResultUpload";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("ProductId")]
        public string ProductId { get; set; }

        [JsonPropertyName("RecipeId")]
        public string RecipeId { get; set; }

        [JsonPropertyName("TotalJudge")]
        public string TotalJudge { get; set; } = "Pass";

        [JsonPropertyName("ProductType")]
        public string ProductType { get; set; } = "";

        [JsonPropertyName("ProcessStartTime")]
        public string ProcessStartTime { get; set; } = "";

        [JsonPropertyName("ProcessEndTime")]
        public string ProcessEndTime { get; set; } = "";

        public MeasureResultData()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }

    public class MeasureResultItem
    {
        [JsonPropertyName("DataId")]
        public string DataId { get; set; }

        [JsonPropertyName("ItemName")]
        public string ItemName { get; set; }

        [JsonPropertyName("ItemValue")]
        public string ItemValue { get; set; }

        [JsonPropertyName("UpperSpecLimit")]
        public string UpperSpecLimit { get; set; }

        [JsonPropertyName("LowerSpecLimit")]
        public string LowerSpecLimit { get; set; }

        [JsonPropertyName("ItemJudge")]
        public string ItemJudge { get; set; }

        [JsonPropertyName("ProcessStartTime")]
        public string ProcessStartTime { get; set; }

        [JsonPropertyName("ProcessEndTime")]
        public string ProcessEndTime { get; set; }

        [JsonPropertyName("DuringTime")]
        public string DuringTime { get; set; }
    }

    public class BumperResultsData
    {
        [JsonPropertyName("Barcode")]
        public string Barcode { get; set; }

        [JsonPropertyName("BumperPosition")]
        public string BumperPosition { get; set; }

        [JsonPropertyName("Layer")]
        public string Layer { get; set; }

        [JsonPropertyName("ItemName")]
        public string ItemName { get; set; }

        [JsonPropertyName("ItemValue")]
        public string ItemValue { get; set; }

        [JsonPropertyName("UpperSpecLimit")]
        public string UpperSpecLimit { get; set; }

        [JsonPropertyName("LowerSpecLimit")]
        public string LowerSpecLimit { get; set; }

        [JsonPropertyName("ItemJudge")]
        public string ItemJudge { get; set; }

        [JsonPropertyName("ProcessStartTime")]
        public string ProcessStartTime { get; set; }

        [JsonPropertyName("ProcessEndTime")]
        public string ProcessEndTime { get; set; } 

        [JsonPropertyName("DuringTime")]
        public string DuringTime { get; set; }

        //public BumperResultsData()
        //{

        //    DuringTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        //    DuringTime = ProcessEndTime - ProcessStartTime;
        //}
    }

    public class MeasureResultUploadResponse
    {
        [JsonPropertyName("EventType")]
        public string EventType { get; set; } = "MeasureResultUpload";

        [JsonPropertyName("RecordId")]
        public string RecordId { get; set; } = "0001";

        [JsonPropertyName("RecordDateTime")]
        public string RecordDateTime { get; set; }

        [JsonPropertyName("EquipmentId")]
        public string EquipmentId { get; set; }

        [JsonPropertyName("ProductId")]
        public string ProductId { get; set; }

        [JsonPropertyName("RecipeId")]
        public string RecipeId { get; set; }

        [JsonPropertyName("TotalJudge")]
        public string TotalJudge { get; set; } = "Pass";

        [JsonPropertyName("ProductType")]
        public string ProductType { get; set; } = "";

        [JsonPropertyName("ProcessStartTime")]
        public string ProcessStartTime { get; set; } = "";

        [JsonPropertyName("ProcessEndTime")]
        public string ProcessEndTime { get; set; } = "";

        [JsonPropertyName("MeasureResultList")]
        public List<MeasureResultItem> MeasureResultList { get; set; } = new List<MeasureResultItem>();

        public MeasureResultUploadResponse()
        {
            RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
        public ReturnMessage ReturnMessage { get; set; }
    }
    #endregion

    public class ReturnMessage
    {
        [JsonPropertyName("Code")]
        public string Code { get; set; }

        [JsonPropertyName("Message")]
        public string Message { get; set; }
    }
}
