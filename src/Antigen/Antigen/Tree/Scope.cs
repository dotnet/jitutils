using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Antigen.Expressions;
using Antigen.Statements;

namespace Antigen.Tree
{
    /*
     * Scopes need information about their "kind" - which construct pushed a new scope
     * This information is used in parts of Antigen that need to do backtracking - ex.
     * the logic behind generating new label names
     */
    public enum ScopeKind
    {
        ConditionalScope,               //introduced by any "conditional" construct - try/catch, loops, if, switch
        LoopScope,                      //introduced by any  loops
        MethodScope,                    //default, scope introduced by a method
        BracesScope                     //introduced by { }
    }

    /// <summary>
    /// Represents a scope
    /// </summary>
    public class Scope
    {
        // Optional parent scope.
        private Scope parent = null;
        public readonly ScopeKind ScopeType;
        public TestCase TestCase;

        // Combines local variables, properties of members, and array properties.  This is the
        // only list from which GetRandom*Variable will draw -- the other lists are only to track
        // variables so that the TestCase can initialize them and print them.
        private Dictionary<ValueType, List<string>> ListOfVariables = new Dictionary<ValueType, List<string>>();

        // List of local variables in current scope. 
        private Dictionary<ValueType, List<string>> LocalVariables = new Dictionary<ValueType, List<string>>();

        public List<string> LocalVariableNames => LocalVariables.SelectMany(dict => dict.Value).ToList();

        // A mapping of all the primitive fields present in given struct
        // Every time a variable "xyz" of one of the struct is defined, all the fields corresponding to that struct
        // will be added to the scope in the form "xyz.field1", "xyz.field2" and so forth.
        public Dictionary<Tree.ValueType, List<StructField>> StructToFieldsMapping = new Dictionary<Tree.ValueType, List<StructField>>();

        private List<ValueType> ListOfStructTypes = new List<ValueType>();

        // List of string vars in the current scope.
        private List<string> LocalStringVariables = new List<string>();

        public Dictionary<string, List<Expression>> ListOfExpressions = new Dictionary<string, List<Expression>>();

        #region Contructors
        public Scope(TestCase tc)
        {
            TestCase = tc;
            ScopeType = ScopeKind.MethodScope;
        }

        public Scope(TestCase tc, ScopeKind t, Scope parentScope)
        {
            TestCase = tc;
            ScopeType = t;
            parent = parentScope;
        }

        #endregion

        #region Get variables/types from the scope
        public string GetRandomVariable(ValueType variableType)
        {
            var defaultVariable = string.Empty;
            var curr = this;

            while (curr != null)
            {
                if (curr.ListOfVariables.TryGetValue(variableType, out var variables))
                {
                    defaultVariable = variables[PRNG.Next(variables.Count)];

                    if (PRNG.Decide(0.3))
                    {
                        return defaultVariable;
                    }
                }
                curr = curr.parent;
            }
            return defaultVariable;
        }

        public ValueType GetRandomStructType()
        {
            return ListOfStructTypes[PRNG.Next(ListOfStructTypes.Count)];
        }

        public Expression GetRandomExpression(ExprKind exprKind, ValueType exprType)
        {
            var key = $"{Enum.GetName(typeof(ExprKind), exprKind)}_{exprType}";
            Expression expression = null;
            var curr = this;

            while (curr != null)
            {
                if (curr.ListOfExpressions.TryGetValue(key, out List<Expression> expressions))
                {
                    expression = expressions[PRNG.Next(expressions.Count)];
                    if (PRNG.Decide(0.3))
                    {
                        return expression;
                    }
                }
                curr = curr.parent;
            }
            return expression;
        }

        #endregion

        #region Add variables/types to scope

        public void AddExpression(ExprKind exprKind, ValueType exprType, Expression expr)
        {
            var key = $"{Enum.GetName(typeof(ExprKind), exprKind)}_{exprType}";

            if (!ListOfExpressions.ContainsKey(key))
            {
                ListOfExpressions[key] = new List<Expression>();
            }

            ListOfExpressions[key].Add(expr);
        }

        public void AddLocal(ValueType variableType, string variableName)
        {
//#if DEBUG
//            foreach (var valueType in LocalVariables.Keys)
//            {
//                Debug.Assert(!LocalVariables[valueType].Contains(variableName));
//            }

//            foreach (var valueType in ListOfVariables.Keys)
//            {
//                Debug.Assert(!ListOfVariables[valueType].Contains(variableName));
//            }
//#endif
            // Add to local variables
            if (!LocalVariables.ContainsKey(variableType))
            {
                LocalVariables[variableType] = new List<string>();
            }

            LocalVariables[variableType].Add(variableName);

            // Add to variables
            if (!ListOfVariables.ContainsKey(variableType))
            {
                ListOfVariables[variableType] = new List<string>();
            }

            ListOfVariables[variableType].Add(variableName);
        }

        /// <summary>
        ///     Adds the struct type <paramref name="typeName"/> in the list of types
        ///     defined in current scope.
        ///     
        ///     It also resolves all the fields present in <paramref name="structFields"/>
        ///     and store the fully qualifier name in <see cref="StructToFieldsMapping"/>.
        ///     
        ///     Returns the new structType created.
        /// </summary>
        public ValueType AddStructType(string typeName, List<StructField> structFields)
        {
            ValueType newStructType = ValueType.CreateStructType(typeName);
            List<StructField> fieldsInCurrStruct = new List<StructField>();

            foreach (StructField field in structFields)
            {
                if (StructToFieldsMapping.TryGetValue(field.FieldType, out List<StructField> childFields))
                {
                    // If the field type is present in structToFieldsMapping, meaning the field is a struct
                    Debug.Assert(field.FieldType.PrimitiveType == Primitive.Struct);

                    foreach (StructField childField in childFields)
                    {
                        // structs present in structToFieldsMapping should have all the child fields expanded.
                        //Debug.Assert((childField.FieldType.PrimitiveType & Primitive.Any) != 0);

                        string expandedFieldName = field.FieldName + "." + childField.FieldName;

                        fieldsInCurrStruct.Add(new StructField(childField.FieldType, expandedFieldName));
                    }
                }
                else
                {
                    // else it is a primitive
                    //Debug.Assert((field.FieldType.PrimitiveType & Primitive.Any) != 0);

                    //string expandedFieldName = field.FieldName + "." + childField.FieldName;

                    fieldsInCurrStruct.Add(new StructField(field.FieldType, field.FieldName));
                }
            }

            Debug.Assert(!StructToFieldsMapping.ContainsKey(newStructType));
            StructToFieldsMapping[newStructType] = fieldsInCurrStruct;

            ListOfStructTypes.Add(newStructType);
            return newStructType;
        }
        #endregion

        #region Aggregate Scopes methods
        /// <summary>
        ///     Combines the list of Variables from all parent scopes.
        /// </summary>
        /// <returns></returns>
        ///  
        private List<string> GetUsableVariables(ValueType variableType)
        {
            List<string> variables = new List<string>();
            Scope curr = this;

            while (curr != null)
            {
                if (curr.ListOfVariables.ContainsKey(variableType))
                {
                    variables.AddRange(curr.ListOfVariables[variableType]);
                }
                curr = curr.parent;
            }
            return variables;
        }

        /// <summary>
        ///     Counts number of struct types defined so far in
        ///     current scope or parent scope.
        /// </summary>
        public int NumOfStructTypes
        {
            get
            {
                int count = 0;

                Scope curr = this;

                while (curr != null)
                {
                    count += curr.ListOfStructTypes.Count;
                    curr = curr.parent;
                }
                return count;
            }
        }

        /// <summary>
        ///     Returns all the struct types defined in current
        ///     and parent scopes.
        /// </summary>
        public List<ValueType> AllStructTypes
        {
            get
            {
                List<ValueType> result = new List<ValueType>();
                Scope curr = this;

                while (curr != null)
                {
                    result.AddRange(curr.ListOfStructTypes);
                    curr = curr.parent;
                }
                return result;
            }
        }

        /// <summary>
        ///     Get all the fields (having fully qualifier name) present in
        ///     <paramref name="structType"/>.
        /// </summary>
        public List<StructField> GetStructFields(Tree.ValueType structType)
        {
            List<StructField> result = null;
            Scope curr = this;

            while (curr != null)
            {
                if (curr.StructToFieldsMapping.TryGetValue(structType, out result))
                {
                    return result;
                }

                curr = curr.parent;
            }
            return result;
        }
        #endregion
    }
}
