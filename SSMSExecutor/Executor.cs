﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.SqlServer.TransactSql.ScriptDom;
//using Microsoft.Data.Schema.ScriptDom;
//using Microsoft.Data.Schema.ScriptDom.Sql;


namespace Devvcat.SSMS
{
    sealed class Executor
    {
        public readonly string CMD_QUERY_EXECUTE = "Query.Execute";

        private EnvDTE.Document document;

        private EnvDTE.EditPoint oldAnchor;
        private EnvDTE.EditPoint oldActivePoint;

        public Executor(EnvDTE.Document document)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));

            var selection = (EnvDTE.TextSelection)this.document.Selection;
            oldAnchor = selection.AnchorPoint.CreateEditPoint();
            oldActivePoint = selection.ActivePoint.CreateEditPoint();
        }

        private CaretPosition GetCaretPosition()
        {
            var anchor = ((EnvDTE.TextSelection)document.Selection).ActivePoint;

            return new CaretPosition
            {
                Line = anchor.Line,
                LineCharOffset = anchor.LineCharOffset
            };
        }

        private string GetDocumentContent()
        {
            var content = string.Empty;
            var selection = (EnvDTE.TextSelection)document.Selection;

            if (!selection.IsEmpty)
            {
                content = selection.Text;
            }
            else
            {
                selection.SelectAll();
                content = selection.Text;

                // restore selection
                selection.MoveToAbsoluteOffset(oldAnchor.AbsoluteCharOffset);
                selection.SwapAnchor();
                selection.MoveToAbsoluteOffset(oldActivePoint.AbsoluteCharOffset, true);
            }

            return content;
        }

        private void MakeSelection(CaretPosition topPoint, CaretPosition bottomPoint)
        {
            var selection = (EnvDTE.TextSelection)document.Selection;

            selection.MoveToLineAndOffset(topPoint.Line, topPoint.LineCharOffset);
            selection.SwapAnchor();
            selection.MoveToLineAndOffset(bottomPoint.Line, bottomPoint.LineCharOffset, true);
        }

        private bool ParseStatements(string script, out StatementList statementList)
        {
            IList<ParseError> errors;
            TSql100Parser parser = new TSql100Parser(true);

            using (System.IO.StringReader reader = new System.IO.StringReader(script))
            {
                statementList = parser.ParseStatementList(reader, out errors);
            }

            return errors.Count == 0;
        }

        private CaretCurrentStatement FindCurrentStatement(StatementList statementList, CaretPosition caret)
        {
            if (statementList == null) return null;

            foreach (var statement in statementList.Statements)
            {
                var ft = statementList.ScriptTokenStream[statement.FirstTokenIndex];
                var lt = statementList.ScriptTokenStream[statement.LastTokenIndex];

                if (caret.Line >= ft.Line && caret.Line <= lt.Line)
                {
                    var isBeforeFirstToken = caret.Line == ft.Line && caret.LineCharOffset < ft.Column;
                    var isAfterLastToken = caret.Line == lt.Line && caret.LineCharOffset > lt.Column + lt.Text.Length;

                    if (!(isBeforeFirstToken || isAfterLastToken))
                    {
                        var currentStatement = new CaretCurrentStatement()
                        {
                            FirstToken = new CaretPosition
                            {
                                Line = ft.Line,
                                LineCharOffset = ft.Column
                            },

                            LastToken = new CaretPosition
                            {
                                Line = lt.Line,
                                LineCharOffset = lt.Column + lt.Text.Length
                            }
                        };
                        return currentStatement;
                    }
                }
            }

            return null;
        }

        private void Exec()
        {
            document.DTE.ExecuteCommand(CMD_QUERY_EXECUTE);
        }

        private bool CanExecute()
        {
            try
            {
                var cmd = document.DTE.Commands.Item(CMD_QUERY_EXECUTE, -1);
                return cmd.IsAvailable;
            }
            catch
            { }

            return false;
        }

        public void ExecuteCurrentStatement()
        {
            if (!CanExecute())
            {
                return;
            }

            if (!(document.Selection as EnvDTE.TextSelection).IsEmpty)
            {
                Exec();
            }
            else
            {
                var caret = GetCaretPosition();
                var script = GetDocumentContent();

                if (ParseStatements(script, out StatementList statementList))
                {
                    var currentStatement = FindCurrentStatement(statementList, caret);

                    if (currentStatement != null)
                    {
                        // select the statement to be executed
                        MakeSelection(currentStatement.FirstToken, currentStatement.LastToken);

                        // execute the statement
                        Exec();

                        // restore selection
                        MakeSelection(
                            new CaretPosition { Line = oldAnchor.Line, LineCharOffset = oldAnchor.LineCharOffset },
                            new CaretPosition { Line = oldActivePoint.Line, LineCharOffset = oldActivePoint.LineCharOffset });
                    }
                }
                else
                {
                    // there are syntax errors
                    // execute anyway to show the errors
                    Exec();
                }
            }
        }

        public class CaretPosition
        {
            public int Line { get; set; }
            public int LineCharOffset { get; set; }
        }

        public class CaretCurrentStatement
        {
            public CaretPosition FirstToken { get; set; }
            public CaretPosition LastToken { get; set; }
        }
    }
}
