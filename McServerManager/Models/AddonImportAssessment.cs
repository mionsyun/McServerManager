namespace McServerManager.Models;

public sealed class AddonImportAssessment
{
    public int CandidateCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool RequiresConfirmation => Warnings.Count > 0;

    public string Summary
    {
        get
        {
            if (CandidateCount == 0)
            {
                return "追加可能なjarファイルが見つかりませんでした。";
            }

            if (Warnings.Count == 0)
            {
                return $"追加前チェックOK（{CandidateCount}件）";
            }

            return $"追加前チェック: 注意 {Warnings.Count} 件（対象 {CandidateCount}件）";
        }
    }
}
