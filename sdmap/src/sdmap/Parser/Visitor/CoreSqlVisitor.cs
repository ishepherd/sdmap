﻿using sdmap.Functional;
using sdmap.Parser.G4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using static sdmap.Parser.G4.SdmapParser;
using Antlr4.Runtime.Misc;
using sdmap.Parser.Utils;
using System.Text;
using sdmap.Runtime;
using sdmap.Macros;

namespace sdmap.Parser.Visitor
{
    public class CoreSqlVisitor : SdmapParserBaseVisitor<Result>
    {
        private readonly SdmapContext _context;
        private ILGenerator _il;

        public EmitFunction Function { get; private set; }

        private CoreSqlVisitor(SdmapContext context)
        {
            _context = context;
        }        

        public override Result VisitNamedSql([NotNull] NamedSqlContext context)
        {
            var openSql = context.GetToken(OpenNamedSql, 0);
            var id = LexerUtil.GetOpenSqlId(openSql.GetText());
            var fullName = _context.GetFullName(id);

            var method = new DynamicMethod(fullName, typeof(string), new[] { typeof(object) });
            _il = method.GetILGenerator();

            var coreSqlContext = context.GetChild<CoreSqlContext>(0);
            
            _il.DeclareLocal(typeof(string));
            _il.Emit(OpCodes.Ldstr, string.Empty);
            _il.Emit(OpCodes.Stloc_0);

            return Visit(coreSqlContext)
                .OnSuccess(() =>
                {
                    _il.Emit(OpCodes.Ret);
                    Function = (EmitFunction)method.CreateDelegate(typeof(EmitFunction));
                });
        }

        public override Result VisitMacro([NotNull] MacroContext context)
        {
            var openMacro = context.GetToken(OpenMacro, 0);

            var macroName = LexerUtil.GetOpenMacroId(openMacro.GetText());
            var arguments = context.GetRuleContexts<MacroParameterContext>()
                .Select<MacroParameterContext, object>(x =>
                {
                    if (x.SYNTAX() != null)
                    {
                        return x.SYNTAX().GetText();
                    }
                    else if (x.NSSyntax() != null)
                    {
                        return x.NSSyntax().GetText();
                    }
                    else if (x.STRING() != null)
                    {
                        return StringUtil.Parse(x.STRING().GetText());
                    }
                    else if (x.NUMBER() != null)
                    {
                        return NumberUtil.Parse(x.NUMBER().GetText());
                    }
                    else if (x.DATE() != null)
                    {
                        return DateUtil.Parse(x.DATE().GetText());
                    }
                    else if (x.unnamedSql() != null)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }).ToArray();

            var macroManagerType = typeof(MacroManager).GetTypeInfo();
            _il.Emit(OpCodes.Call, macroManagerType.GetMethod(
                nameof(MacroManager.GetDefaultInstance)));
            _il.Emit(OpCodes.Ldstr, macroName);
            
            _il.Emit(OpCodes.Call, macroManagerType.GetMethod(
                nameof(MacroManager.Execute)));

            throw new NotImplementedException();
            //return _context.MacroContext.Execute(macroName, _context, null, arguments)
            //    .OnSuccess(str =>
            //    {
            //        PushResult(str);
            //    });
        }

        public override Result VisitPlainText([NotNull] PlainTextContext context)
        {
            var sqlText = context.GetToken(SQLText, 0);
            PushResult(sqlText.GetText());
            return Result.Ok();
        }

        private void PushResult(string text)
        {
            _il.Emit(OpCodes.Ldloc_0);
            _il.Emit(OpCodes.Ldstr, text);
            _il.Emit(OpCodes.Call,
                typeof(string).GetTypeInfo().GetMethod(
                    nameof(string.Concat), new[] { typeof(string), typeof(string) }));
        }

        public static CoreSqlVisitor Create(SdmapContext context)
        {
            return new CoreSqlVisitor(context);
        }

        public static Result<EmitFunction> Compile(NamedSqlContext parseTree, SdmapContext context)
        {
            var visitor = Create(context);
            return visitor.Visit(parseTree)
                .OnSuccess(() => visitor.Function);
        }

        public static CoreSqlVisitor CreateEmpty()
        {
            return new CoreSqlVisitor(SdmapContext.CreateEmpty());
        }
    }
}
