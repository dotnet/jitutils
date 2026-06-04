using Antigen.Tree;

namespace Antigen.Expressions
{
    public class Expression : Node
    {
#if DEBUG_TODO
        private readonly Dictionary<string, int> _expressionsCount = new();
#endif

        public Expression(TestCase testCase) : base(testCase)
        {

        }

        protected override string Annotate()
        {
#if DEBUG_TODO
            string typeName = GetType().Name;
            if (!_expressionsCount.ContainsKey(typeName))
            {
                _expressionsCount[typeName] = 0;
            }
            _expressionsCount[typeName]++;
            return $"{ToString() /* E#{_expressionsCount[typeName]}: {typeName} */}";
#else
            return ToString();
#endif
        }

        public override string ToString()
        {
            return string.Empty;
        }
    }
}
