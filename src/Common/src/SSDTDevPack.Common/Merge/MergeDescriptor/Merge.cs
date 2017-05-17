﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SSDTDevPack.Common.Dac;
using SSDTDevPack.Common.ProjectVersion;

namespace SSDTDevPack.Merge.MergeDescriptor
{
    public class InScriptDescriptor
    {
        public InScriptDescriptor(int scriptOffset, int scriptLength, string filePath)
        {
            ScriptOffset = scriptOffset;
            ScriptLength = scriptLength;
            FilePath = filePath;

            using (var reader = new StreamReader(filePath))
            {
                OriginalText = reader.ReadToEnd().Substring(scriptOffset, scriptLength);
            }

        }

        public int ScriptOffset { get; set; }
        public int ScriptLength { get; set; }
        public string FilePath { get; set; }
        public string OriginalText { get; set; }
    }

    public class Merge
    {
        public MergeStatement Statement { get; set; }

        public InScriptDescriptor ScriptDescriptor { get; set; }

        public DataTable Data { get; set; }

        public TableDescriptor Table;

        public Identifier Name;

        public MergeOptions Option { get; set; }

        public MergeStatement CustommMerger { get; set; } //used when we read from disk as they may have changed 
                                                          //something and so we just want to control which conditions are shown and the data
    }

    public class MergeWriter
    {
        private readonly Merge _merge;

        public MergeWriter(Merge merge)
        {
            _merge = merge;
        }

        public void Write()
        {       
            var merge = _merge.CustommMerger == null ? CreateMerge() : UpdateMerge();

            var script = merge.GetScript();

            var scriptFile = GetScriptFile(_merge.ScriptDescriptor.FilePath);

            if (!String.IsNullOrEmpty(_merge.ScriptDescriptor.OriginalText))
            {
                scriptFile = scriptFile.Replace(_merge.ScriptDescriptor.OriginalText, script);
            }
            else
            {
                if (!scriptFile.EndsWith("\r\n"))
                    scriptFile += "\r\n";
                                  
                scriptFile += "GO\r\n" + script;
            }

            WriteScriptFile(_merge.ScriptDescriptor.FilePath, scriptFile);

            _merge.ScriptDescriptor.OriginalText = script;

        }
        
        private void WriteScriptFile(string filePath, string scriptFile)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.Write(scriptFile);
                writer.Flush();
                writer.Close();
            }
        }

        private string GetScriptFile(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            {
                return reader.ReadToEnd();
            }
        }

        private MergeStatement UpdateMerge()
        {
            var merge = _merge.CustommMerger;

            merge.MergeSpecification.ActionClauses.Clear();
            BuildActions(merge.MergeSpecification);
            SetInlineTableData(merge.MergeSpecification);
            return merge;
        }


        private MergeStatement CreateMerge()
        {


            var merge =  new MergeStatement();
            var specification = merge.MergeSpecification = new MergeSpecification();
            
            SetTableAlias(specification);

            SetInlineTableReference(specification);
                
            SetTargetName(specification);

            BuildSearchCondition(specification);

            BuildActions(specification);

            SetInlineTableData(specification);

            return merge;
        }

        private void BuildActions(MergeSpecification specification)
        {


            if(_merge.Option.HasInsert)
                BuildInsertAction(specification);

            if (_merge.Option.HasUpdate)
                BuildUpdateAction(specification);

            if (_merge.Option.HasDelete)
                BuildDeleteAction(specification);
        }

        private void BuildDeleteAction(MergeSpecification specification)
        {
            var action = new DeleteMergeAction();
            specification.ActionClauses.Add(new MergeActionClause
            {
                Action = action,
                Condition = MergeCondition.NotMatchedBySource
            });
        }

        private void BuildUpdateAction(MergeSpecification specification)
        {
            var clause = new MergeActionClause();
            var expression =
                (clause.SearchCondition = new BooleanParenthesisExpression()) as BooleanParenthesisExpression;

            var isNulls = BuildNullIfStatements();

            if (isNulls.Count > 1)
            {
                var expressions = CreateExpressionTreeForUpdateSearch(isNulls);

                //Save the first expression
                expression.Expression = expressions.First();
                clause.SearchCondition = expression;
            }
            else
            {
                clause.SearchCondition = isNulls[0];
            }
            clause.Condition = MergeCondition.Matched;
            clause.Action = CreateUpdateSetActions(clause);

            specification.ActionClauses.Add(clause);
        }

        private void BuildInsertAction(MergeSpecification specification)
        {
            var action = new InsertMergeAction();
            var insertSource = action.Source = new ValuesInsertSource();
            var row = new RowValue();

            foreach (var column in _merge.Table.Columns)
            {
                var colRef = new ColumnReferenceExpression();
                colRef.ColumnType = ColumnType.Regular;
                colRef.MultiPartIdentifier = MultiPartIdentifierBuilder.Get(column.Name.GetName());
                action.Columns.Add(colRef);

                colRef = new ColumnReferenceExpression();
                colRef.ColumnType = ColumnType.Regular;
                colRef.MultiPartIdentifier = MultiPartIdentifierBuilder.Get(MergeIdentifierStrings.SourceName, column.Name.GetName());

                row.ColumnValues.Add(colRef);
            }

            insertSource.RowValues.Add(row);

            specification.ActionClauses.Add(new MergeActionClause
            {
                Action = action,
                Condition = MergeCondition.NotMatchedByTarget
            });
        }


        //The isNulls are used in the search condition to find out if any of the columns are different and therefore need an update
        private List<BooleanIsNullExpression> BuildNullIfStatements()
        {
            var isNulls = new List<BooleanIsNullExpression>();

            foreach (var descriptor in _merge.Table.Columns)
            {
                var nullExpression = new NullIfExpression();

                //var first =
                //    (nullExpression.FirstExpression = new ColumnReferenceExpression()) as ColumnReferenceExpression;
                //first.MultiPartIdentifier =  MultiPartIdentifierBuilder.Get(MergeIdentifierStrings.SourceName, descriptor.Name.GetName());

                //var second =
                //    (nullExpression.SecondExpression = new ColumnReferenceExpression()) as ColumnReferenceExpression;
                //second.MultiPartIdentifier = MultiPartIdentifierBuilder.Get(MergeIdentifierStrings.TargetName, descriptor.Name.GetName());

                nullExpression.FirstExpression = GetColumnOrCastColumn(descriptor, MergeIdentifierStrings.SourceName);
                nullExpression.SecondExpression = GetColumnOrCastColumn(descriptor, MergeIdentifierStrings.TargetName);

                var isNullExpresson = new BooleanIsNullExpression();
                isNullExpresson.Expression = nullExpression;
                isNullExpresson.IsNot = true;
                isNulls.Add(isNullExpresson);
            }

            return isNulls;
        }

        private ScalarExpression GetColumnOrCastColumn(ColumnDescriptor column, string tableName)
        {
            var columnReference = new ColumnReferenceExpression();
            columnReference.MultiPartIdentifier = MultiPartIdentifierBuilder.Get(tableName, column.Name.GetName());

            if (column.UnderlyingType == "xml")
            {
                var cast = new CastCall();
                var type = new SqlDataTypeReference();
                type.SqlDataTypeOption = SqlDataTypeOption.NVarChar;
                type.Parameters.Add(new MaxLiteral());
                
                cast.DataType = type;
                cast.Parameter = columnReference;
                return cast;
            }

            return columnReference;
        }


        private static List<BooleanBinaryExpression> CreateExpressionTreeForUpdateSearch(List<BooleanIsNullExpression> isNulls)
        {
            var expressions = new List<BooleanBinaryExpression>();
            BooleanBinaryExpression last = null;
            var counter = 0;

            foreach (var isNull in isNulls)
            {
                var boolExpression = new BooleanBinaryExpression
                {
                    SecondExpression = isNull,
                    BinaryExpressionType = BooleanBinaryExpressionType.Or
                };

                if (last != null)
                {
                    if (isNulls.Count - 1 == ++counter)
                    {
                        last.FirstExpression = isNulls.Last();
                    }
                    else
                    {
                        last.FirstExpression = boolExpression;
                    }
                }

                last = boolExpression;
                expressions.Add(last);
            }

            return expressions;
        }
        private UpdateMergeAction CreateUpdateSetActions(MergeActionClause clause)
        {
            var action = (clause.Action = new UpdateMergeAction()) as UpdateMergeAction;
            foreach (var col in _merge.Table.Columns)
            {
                var setClause = new AssignmentSetClause();
                setClause.AssignmentKind = AssignmentKind.Equals;

                var identifier = MultiPartIdentifierBuilder.Get(MergeIdentifierStrings.TargetName, col.Name.GetName());

                setClause.Column = new ColumnReferenceExpression();
                setClause.Column.MultiPartIdentifier = identifier;

                var newValue = (setClause.NewValue = new ColumnReferenceExpression()) as ColumnReferenceExpression;
                newValue.MultiPartIdentifier = MultiPartIdentifierBuilder.Get(MergeIdentifierStrings.SourceName, col.Name.GetName());
                
                action.SetClauses.Add(setClause);
            }
            return action;
        }

        private void BuildSearchCondition(MergeSpecification specification)
        {
            if (_merge.Table.Columns.Count(p => p.IsKey) > 1)
            {
                BuildMultiKeySearchCondition(specification);
                return;
            }

            if (_merge.Table.Columns.Count(p => p.IsKey) == 0)
            {
                //OutputWindowMessage.WriteMessage("The table: {0} does not contain a primary key so it isn't possible to work out what the columns the merge should check.",
                //    _targetTableName);

                CreateSearchConditionForTableWithNoKeys((specification.SearchCondition = new BooleanComparisonExpression()) as BooleanComparisonExpression);
                return;
            }

           CreateSearchCondition(_merge.Table.Columns.FirstOrDefault(p=>p.IsKey).Name.GetName(), (specification.SearchCondition = new BooleanComparisonExpression()) as BooleanComparisonExpression);
        }

        private BooleanComparisonExpression CreateSearchConditionForTableWithNoKeys(BooleanComparisonExpression condition)
        {
            condition.ComparisonType = BooleanComparisonType.Equals;
            condition.FirstExpression = new ScalarExpressionSnippet { Script = string.Format("{0}.{1}", MergeIdentifierStrings.SourceName, "[???]") };
            condition.SecondExpression = new ScalarExpressionSnippet { Script = string.Format("{0}.{1}", MergeIdentifierStrings.TargetName, "[???]") };
            return condition;
        }


        private void BuildMultiKeySearchCondition(MergeSpecification specification)
        {
            var comparisons = new List<BooleanComparisonExpression>();

            foreach (var column in _merge.Table.Columns.Where(p => p.IsKey))
            {
                var condition = new BooleanComparisonExpression();
                comparisons.Add(CreateSearchCondition(column.Name.GetName(), condition));
            }

            specification.SearchCondition = CreateExpressionTreeForMultiKeySearchConditon(comparisons).First();
        }

        private BooleanComparisonExpression CreateSearchCondition(string keyColumn, BooleanComparisonExpression condition)
        {

            condition.ComparisonType = BooleanComparisonType.Equals;
            condition.FirstExpression = new ScalarExpressionSnippet { Script = string.Format("[{0}].[{1}]", MergeIdentifierStrings.SourceName, keyColumn) };
            condition.SecondExpression = new ScalarExpressionSnippet { Script = string.Format("[{0}].[{1}]", MergeIdentifierStrings.TargetName, keyColumn) };
            return condition;
        }

        private static List<BooleanBinaryExpression> CreateExpressionTreeForMultiKeySearchConditon(List<BooleanComparisonExpression> booleanComparisons)
        {
            var expressions = new List<BooleanBinaryExpression>();
            BooleanBinaryExpression last = null;
            var counter = 0;
            foreach (var isNull in booleanComparisons)
            {
                var boolExpression = new BooleanBinaryExpression
                {
                    SecondExpression = isNull,
                    BinaryExpressionType = BooleanBinaryExpressionType.And
                };

                if (last != null)
                {
                    if (booleanComparisons.Count - 1 == ++counter)
                    {
                        last.FirstExpression = booleanComparisons.Last();
                    }
                    else
                    {
                        last.FirstExpression = boolExpression;
                    }
                }

                last = boolExpression;
                expressions.Add(last);
            }

            return expressions;
        }



        private void SetTargetName(MergeSpecification specification)
        {
            var table = (specification.Target = new NamedTableReference()) as NamedTableReference;
            table.SchemaObject = _merge.Table.Name.ToSchemaObjectName();
        }

        
        private void SetInlineTableReference(MergeSpecification specification)
        {
            var table = (specification.TableReference = new InlineDerivedTable()) as InlineDerivedTable;
            table.Alias = new Identifier { Value = MergeIdentifierStrings.SourceName };

            foreach (var col in _merge.Table.Columns)
            {
                table.Columns.Add(col.Name.ToIdentifier());
            }
        }

        private void SetTableAlias(MergeSpecification specification)
        {
            specification.TableAlias = new Identifier { Value = MergeIdentifierStrings.TargetName };
        }

        public void SetInlineTableData(MergeSpecification specification)
        {
            var table = specification.TableReference as InlineDerivedTable;

            if (null == table)
                throw new NotImplementedException("only support merges from inline table reference");

            table.RowValues.Clear();
            
            foreach (DataRow row in _merge.Data.Rows)
            {
                if (row.RowState == DataRowState.Deleted)
                    continue;

                if (row.HasErrors)
                    continue;

                var rowValue = new RowValue();

                var colIndex = 0;
                foreach (var col in _merge.Table.Columns)
                {
                    var value = row[col.Name.GetName()];
                    if (value == DBNull.Value)
                    {
                        rowValue.ColumnValues.Add(new NullLiteral());
                    }
                    else
                    {
                        var actualColumn = _merge.Data.Columns[col.Name.GetName()];

                        rowValue.ColumnValues.Add(GetColumnValue(GetStringRepresentation(value, actualColumn), col.DataType, col.IsNText));
                    }

                    colIndex++;
                }

                table.RowValues.Add(rowValue);
            }
        }

        private static string GetStringRepresentation(object value, DataColumn actualColumn)
        {
            if (IsBitTypeMasqueradingAsBool(actualColumn))
            {
                return ((bool)value) ? "1" : "0";
            }

            return value.ToString();
        }

        private static bool IsBitTypeMasqueradingAsBool(DataColumn actualColumn)
        {
            return actualColumn.DataType == typeof (bool);
        }

        private static ScalarExpression GetColumnValue(string value, LiteralType type, bool isUnicode)
        {
            switch (type)
            {
                case LiteralType.Integer:
                    return new IntegerLiteral { Value = value };
                case LiteralType.Real:
                    return new RealLiteral { Value = value };
                case LiteralType.Money:
                    return new MoneyLiteral { Value = value };
                case LiteralType.Binary:
                    return new BinaryLiteral { Value = value };
                case LiteralType.String:
                    return  new StringLiteral { Value = value , IsNational = isUnicode};
                case LiteralType.Null:
                    return new NullLiteral { Value = value };
                case LiteralType.Default:
                    return new DefaultLiteral { Value = value };
                case LiteralType.Max:
                    return new MaxLiteral { Value = value };
                case LiteralType.Odbc:
                    return new OdbcLiteral { Value = value };
                case LiteralType.Identifier:
                    return new IdentifierLiteral { Value = value };
                case LiteralType.Numeric:
                    return new NumericLiteral { Value = value };
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public class MergeIdentifierStrings
    {
        public const string TargetName = "TARGET";
        public const string SourceName = "SOURCE";
    }

    public class MultiPartIdentifierBuilder
    {
        public static MultiPartIdentifier Get(string part1)
        {
            var identifier = new MultiPartIdentifier();
            identifier.Identifiers.Add(new Identifier() { Value = Quote.Name(part1) });
            
            return identifier;
        }

        public static MultiPartIdentifier Get(string part1, string part2)
        {
            var identifier = new MultiPartIdentifier();
            identifier.Identifiers.Add(new Identifier() { Value = Quote.Name(part1) });
            identifier.Identifiers.Add(new Identifier() { Value = Quote.Name(part2)});

            return identifier;
        }

        public static MultiPartIdentifier Get(string part1, Identifier part2)
        {
            var identifier = new MultiPartIdentifier();
            identifier.Identifiers.Add(new Identifier() { Value = Quote.Name(part1) });
            identifier.Identifiers.Add(part2);

            return identifier;
        }
    }


    public static class MergeStatementDesriptorToScriptExtensions
    {
        public static string GetScript(this MergeStatement source)
        {
            var script = "";
            var generator =
                VersionDetector.ScriptGeneratorFactory(new SqlScriptGeneratorOptions
                {
                    IncludeSemicolons = true,
                    AlignClauseBodies = true,
                    AlignColumnDefinitionFields = true,
                    AsKeywordOnOwnLine = true,
                    MultilineInsertSourcesList = true,
                    MultilineInsertTargetsList = true,
                    MultilineSelectElementsList = true,
                    NewLineBeforeOpenParenthesisInMultilineList = true
                });
            generator.GenerateScript(source, out script);

            if (!script.EndsWith(";"))
            {
                script = script + ";";
            }
            return script;
        }

       
    }


}
