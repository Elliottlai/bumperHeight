using System.Runtime.InteropServices;
using BarcodeReader.Interfaces;
using MvCodeReaderSDKNet;

namespace BarcodeReader.Services;

/// <summary>
/// MvCodeReaderSDKNet ªº IDeviceEnumerator ¹ê§@
/// </summary>
public sealed class MvDeviceEnumerator : IDeviceEnumerator
{
    public IReadOnlyList<DeviceInfoModel> EnumerateDevices()
    {
        var deviceList = new MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
        int ret = MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref deviceList, MvCodeReader.MV_CODEREADER_GIGE_DEVICE);

        if (ret != MvCodeReader.MV_CODEREADER_OK || deviceList.nDeviceNum == 0)
            return [];

        var results = new List<DeviceInfoModel>();

        for (int i = 0; i < deviceList.nDeviceNum; i++)
        {
            var stDevInfo = (MvCodeReader.MV_CODEREADER_DEVICE_INFO)Marshal.PtrToStructure(
                deviceList.pDeviceInfo[i],
                typeof(MvCodeReader.MV_CODEREADER_DEVICE_INFO))!;

            if (stDevInfo.nTLayerType != MvCodeReader.MV_CODEREADER_GIGE_DEVICE)
                continue;

            nint buffer = Marshal.UnsafeAddrOfPinnedArrayElement(stDevInfo.SpecialInfo.stGigEInfo, 0);
            var gigeInfo = (MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO)Marshal.PtrToStructure(
                buffer,
                typeof(MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO))!;

            string displayName = !string.IsNullOrEmpty(gigeInfo.chUserDefinedName)
                ? $"GEV: {gigeInfo.chUserDefinedName} ({gigeInfo.chSerialNumber})"
                : $"GEV: {gigeInfo.chManufacturerName} {gigeInfo.chModelName} ({gigeInfo.chSerialNumber})";

            results.Add(new DeviceInfoModel
            {
                UserDefinedName = gigeInfo.chUserDefinedName ?? string.Empty,
                ManufacturerName = gigeInfo.chManufacturerName ?? string.Empty,
                ModelName = gigeInfo.chModelName ?? string.Empty,
                SerialNumber = gigeInfo.chSerialNumber ?? string.Empty,
                DisplayName = displayName,
                RawDeviceInfo = stDevInfo
            });
        }

        return results;
    }
}