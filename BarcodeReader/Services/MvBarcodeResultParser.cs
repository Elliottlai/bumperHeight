using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using BarcodeReader.Interfaces;
using MvCodeReaderSDKNet;

namespace BarcodeReader.Services;

/// <summary>
/// MvCodeReaderSDKNet ªº IBarcodeResultParser ¹ê§@
/// </summary>
public sealed class MvBarcodeResultParser : IBarcodeResultParser
{
    public IReadOnlyList<BarcodeResult> Parse(nint pFrameInfo)
    {
        var frameInfo = Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2>(pFrameInfo);
        var bcrResult = Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2>(
            frameInfo.UnparsedBcrList.pstCodeListEx2);

        var results = new List<BarcodeResult>();

        for (int i = 0; i < bcrResult.nCodeNum; i++)
        {
            var info = bcrResult.stBcrInfoEx2[i];
            var points = new Point[4];
            for (int j = 0; j < 4; j++)
            {
                points[j] = new Point(info.pt[j].x, info.pt[j].y);
            }

            string code = Encoding.Default.GetString(info.chCode);
            code = code.TrimEnd('\0');

            results.Add(new BarcodeResult
            {
                Code = string.IsNullOrEmpty(code) ? "NoRead" : code,
                BarType = GetBarType((MvCodeReader.MV_CODEREADER_CODE_TYPE)info.nBarType),
                TotalProcessCost = (int)info.nTotalProcCost,
                AlgoCost = info.sAlgoCost.ToString(),
                PPM = info.sPPM.ToString(),
                OverQuality = info.stCodeQuality.nOverQuality,
                IDRScore = (int)info.nIDRScore,
                Points = points
            });
        }

        return results;
    }

    private static string GetBarType(MvCodeReader.MV_CODEREADER_CODE_TYPE barType) => barType switch
    {
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_DM => "DM",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_QR => "QR",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN8 => "EAN8",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCE => "UPCE",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCA => "UPCA",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN13 => "EAN13",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ISBN13 => "ISBN13",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODABAR => "Codabar",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF25 => "ITF25",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE39 => "Code 39",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE93 => "Code 93",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE128 => "Code 128",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_PDF417 => "PDF417",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MATRIX25 => "Matrix25",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MSI => "MSI",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE11 => "Code 11",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_INDUSTRIAL25 => "Industrial25",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CHINAPOST => "ChinaPost",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF14 => "ITF14",
        MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_ECC140 => "ECC140",
        _ => "/"
    };
}