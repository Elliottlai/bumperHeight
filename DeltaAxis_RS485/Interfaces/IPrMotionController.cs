namespace DeltaAxis_RS485.Interfaces;

/// <summary>
/// PR mode 定位控制介面
/// </summary>
public interface IPrMotionController
{
    /// <summary>將 mm 換算成 PUU</summary>
    int MmToPuu(double mm);

    /// <summary>更新 PR#1 的目標位置 (PUU)</summary>
    void UpdatePr1TargetPosition(int puu);

    /// <summary>觸發 PR#1 執行</summary>
    void TriggerPr1();

    /// <summary>等待到位完成（含逾時檢查）</summary>
    void WaitInPosition();

    /// <summary>移動到指定位置 (mm)，整合換算、寫入、觸發、等待</summary>
    void MoveToPositionMm(double mm);
}