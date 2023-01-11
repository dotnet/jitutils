// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace ManagedCodeGen
{
    public abstract class Metric
    {
        public virtual string Name { get; }
        public virtual string DisplayName { get; }
        public virtual string Unit { get; }
        public virtual bool LowerIsBetter { get; }
        public abstract Metric Clone();
        public abstract string ValueString { get; }
        public double Value { get; set; }

        public void Add(Metric m)
        {
            Value += m.Value;
        }

        public void Sub(Metric m)
        {
            Value -= m.Value;
        }

        public void Rel(Metric m)
        {
            Value = (Value - m.Value) / m.Value;
        }

        public void SetValueFrom(Metric m)
        {
            Value = m.Value;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class CodeSizeMetric : Metric
    {
        public override string Name => "CodeSize";
        public override string DisplayName => "Code Size";
        public override string Unit => "byte";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new CodeSizeMetric();
        public override string ValueString => $"{Value}";
    }

    public class PrologSizeMetric : Metric
    {
        public override string Name => "PrologSize";
        public override string DisplayName => "Prolog Size";
        public override string Unit => "byte";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new PrologSizeMetric();
        public override string ValueString => $"{Value}";
    }

    public class PerfScoreMetric : Metric
    {
        public override string Name => "PerfScore";
        public override string DisplayName => "Perf Score";
        public override string Unit => "PerfScoreUnit";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new PerfScoreMetric();
        public override string ValueString => $"{Value:F2}";
    }

    public class InstrCountMetric : Metric
    {
        public override string Name => "InstrCount";
        public override string DisplayName => "Instruction Count";
        public override string Unit => "Instruction";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new InstrCountMetric();
        public override string ValueString => $"{Value}";
    }

    public class AllocSizeMetric : Metric
    {
        public override string Name => "AllocSize";
        public override string DisplayName => "Allocation Size";
        public override string Unit => "byte";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new AllocSizeMetric();
        public override string ValueString => $"{Value}";
    }

    public class ExtraAllocBytesMetric : Metric
    {
        public override string Name => "ExtraAllocBytes";
        public override string DisplayName => "Extra Allocation Size";
        public override string Unit => "byte";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new ExtraAllocBytesMetric();
        public override string ValueString => $"{Value}";
    }
    public class DebugClauseMetric : Metric
    {
        public override string Name => "DebugClauseCount";
        public override string DisplayName => "Debug Clause Count";
        public override string Unit => "Clause";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new DebugClauseMetric();
        public override string ValueString => $"{Value}";
    }

    public class DebugVarMetric : Metric
    {
        public override string Name => "DebugVarCount";
        public override string DisplayName => "Debug Variable Count";
        public override string Unit => "Variable";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new DebugVarMetric();
        public override string ValueString => $"{Value}";
    }

    /* LSRA specific */
    public class SpillCountMetric : Metric
    {
        public override string Name => "SpillCount";
        public override string DisplayName => "Spill Count";
        public override string Unit => "Count";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new SpillCountMetric();
        public override string ValueString => $"{Value}";
    }

    public class SpillWeightMetric : Metric
    {
        public override string Name => "SpillWeight";
        public override string DisplayName => "Spill Weighted";
        public override string Unit => "Count";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new SpillWeightMetric();
        public override string ValueString => $"{Value}";
    }

    public class ResolutionCountMetric : Metric
    {
        public override string Name => "ResolutionCount";
        public override string DisplayName => "Resolution Count";
        public override string Unit => "Count";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new ResolutionCountMetric();
        public override string ValueString => $"{Value}";
    }

    public class ResolutionWeightMetric : Metric
    {
        public override string Name => "ResolutionWeight";
        public override string DisplayName => "Resolution Weighted";
        public override string Unit => "Count";
        public override bool LowerIsBetter => true;
        public override Metric Clone() => new ResolutionWeightMetric();
        public override string ValueString => $"{Value}";
    }
}
