using Selficient.Domain.Households;

namespace Selficient.Domain.Risks;

/// <summary>The risks that apply to a household, highest score first.</summary>
public sealed class RiskAssessment
{
    private readonly HashSet<RiskKind> _kinds;

    internal RiskAssessment(IReadOnlyList<Risk> ranked)
    {
        Ranked = ranked;
        _kinds = [.. ranked.Select(r => r.Kind)];
    }

    /// <summary>Risks in rank order.</summary>
    public IReadOnlyList<Risk> Ranked { get; }

    /// <summary>True when the assessment includes the kind.</summary>
    public bool Includes(RiskKind kind) => _kinds.Contains(kind);
}

/// <summary>Runs every assessor and ranks what applies. Deterministic: score, then kind order.</summary>
public sealed class RiskAssessmentPipeline
{
    private readonly IReadOnlyList<IRiskAssessor> _assessors;

    /// <summary>Creates the pipeline over a fixed set of assessors.</summary>
    public RiskAssessmentPipeline(IEnumerable<IRiskAssessor> assessors)
    {
        ArgumentNullException.ThrowIfNull(assessors);
        _assessors = [.. assessors];
    }

    /// <summary>Assesses the household.</summary>
    public RiskAssessment Run(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        List<Risk> risks = new(_assessors.Count);
        foreach (IRiskAssessor assessor in _assessors)
        {
            Risk? risk = assessor.Assess(profile);
            if (risk is not null)
            {
                risks.Add(risk);
            }
        }

        risks.Sort(static (left, right) =>
        {
            int byScore = right.Score.CompareTo(left.Score);
            return byScore != 0 ? byScore : left.Kind.CompareTo(right.Kind);
        });

        return new RiskAssessment(risks);
    }
}
