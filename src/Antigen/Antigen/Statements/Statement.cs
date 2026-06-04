using Antigen.Tree;

namespace Antigen.Statements
{
    public class Statement : Node
    {

#if DEBUG_TODO
        private readonly Dictionary<string, int> _statementsCount = new();
#endif

        public Statement(TestCase testCase) : base(testCase)
        {

        }


        protected override string Annotate()
        {
#if DEBUG_TODO
            string typeName = GetType().Name;
            if (!_statementsCount.ContainsKey(typeName))
            {
                _statementsCount[typeName] = 0;
            }
            _statementsCount[typeName]++;
            return $"{ToString() /* S#{_statementsCount[typeName]}: {typeName} */}";
#else
            return ToString();
#endif
        }
    }
}
