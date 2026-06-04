// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Antigen.Tree
{
    public abstract class Node
    {
        protected TestCase _testCase;
        //protected string _contents;

        //public virtual void Render(RenderContext renderContext)
        //{

        //}

        //TODO: Have this call an abstract method
        //public override string ToString()
        //{
        //    return string.Empty;
        //}

        protected abstract string Annotate();
        //protected virtual void PopulateContent() { }

        public Node(TestCase tc)
        {
            _testCase = tc;
        }
    }
}
