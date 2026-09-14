using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Contains("--self-test"))
{
    var cases = new (string Input, string Expected)[]
    {
        ("await Run(() =>\n{\n});", "await Run(() => { });"),
        ("try\n{\n    Work();\n}\ncatch\n{\n}", "try\n{\n    Work();\n}\ncatch { }"),
        ("await Run(() =>\n{\n    // Deliberately empty.\n});", "await Run(() =>\n{\n    // Deliberately empty.\n});"),
        ("syntax = Parse(source);\nvar lines = Split(source);\nvar insert = new HashSet<int>();",
         "syntax = Parse(source);\n\nvar lines = Split(source);\nvar insert = new HashSet<int>();"),
        ("var lines = Split(source);\nsyntax = Parse(source);",
         "var lines = Split(source);\n\nsyntax = Parse(source);"),
        ("var first = 1;\n\nvar second = 2;\nUse(first, second);",
         "var first = 1;\nvar second = 2;\n\nUse(first, second);"),
        ("var x = ok ? 1 : 2;", "var x = ok\n    ? 1\n    : 2;"),
        ("var x = a ? b ? 1 : 2 : 3;", "var x = a\n    ? b\n        ? 1\n        : 2\n    : 3;"),
        ("var result = condition ? \"" + new string('a', 70) + "\" : \"" + new string('b', 30) + "\";",
         "var result = condition\n    ? \"" + new string('a', 70) + "\"\n    : \"" + new string('b', 30) + "\";"),
        ("var edits = new List<int>();\nforeach (var property in edits)\n{\n    A();\n}\nConsole.WriteLine(edits);",
         "var edits = new List<int>();\n\nforeach (var property in edits)\n{\n    A();\n}\n\nConsole.WriteLine(edits);"),
        ("var start = 1;\nwhile (start > 0)\n{\n    start--;\n}\nvar output = new List<string>();",
         "var start = 1;\n\nwhile (start > 0)\n{\n    start--;\n}\n\nvar output = new List<string>();"),
        ("var cases = new[]\n{\n    1,\n};\nforeach (var input in cases)\n{\n    if (input == 0)\n    {\n        A();\n    }\n}\nConsole.WriteLine(cases);",
         "var cases = new[]\n{\n    1,\n};\n\nforeach (var input in cases)\n{\n    if (input == 0)\n    {\n        A();\n    }\n}\n\nConsole.WriteLine(cases);"),
        ("A();\ntry\n{\n    A();\n}\ncatch\n{\n    A();\n}\nfinally\n{\n    A();\n}\nB();",
         "A();\n\ntry\n{\n    A();\n}\ncatch\n{\n    A();\n}\nfinally\n{\n    A();\n}\n\nB();"),
        ("A();\ndo\n{\n    A();\n}\nwhile (true);\nB();",
         "A();\n\ndo\n{\n    A();\n}\nwhile (true);\n\nB();"),
        ("class C\n{\n    int P\n    {\n        get;\n        set;\n    }\n}",
         "class C\n{\n    int P { get; set; }\n}"),
        ("class C\n{\n    int P\n    {\n        get;\n    } = 1;\n}",
         "class C\n{\n    int P { get; } = 1;\n}"),
        ("class C\n{\n    void M()\n    {\n        if (true) { return; }\n    }\n}",
         "class C\n{\n    void M()\n    {\n        if (true)\n        {\n            return;\n        }\n    }\n}"),
        ("class C { void M()\n{\n A();\n if (true)\n {\n\n return;\n }\n A();\n}\n}",
         "class C { void M()\n{\n A();\n\n if (true)\n {\n return;\n }\n\n A();\n}\n}"),
        ("class C { void M()\n{\n A();\n return;\n}\n}",
         "class C { void M()\n{\n A();\n\n return;\n}\n}"),
        ("class C { void M()\n{\n if (true)\n {\n return;\n }\n else\n {\n return;\n }\n}\n}",
         "class C { void M()\n{\n if (true)\n {\n return;\n }\n else\n {\n return;\n }\n}\n}"),
    };

    foreach (var (input, expected) in cases)
    {
        if (Format(input) != expected || Format(expected) != expected)
        {
            throw new InvalidOperationException("Formatting regression: " + input);
        }
    }

    Console.WriteLine($"{cases.Length} formatting regression cases passed, including idempotence.");

    return 0;
}

var check = args.Contains("--check");
var root = args.First(a => !a.StartsWith("--", StringComparison.Ordinal));
var changed = 0;

foreach (var folder in new[] { "apps", "tests", "tools" })
{
    foreach (var path in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\")))
    {
        var original = File.ReadAllText(path);
        var result = Format(original);

        if (original != result)
        {
            changed++;

            if (!check)
            {
                File.WriteAllText(path, result);
            }

            Console.WriteLine(Path.GetRelativePath(root, path));
        }
    }
}

Console.WriteLine(check
    ? $"{changed} file(s) need formatting."
    : $"Formatted {changed} file(s).");

return check && changed > 0
    ? 1
    : 0;

static string Format(string original)
{
    var source = original;
    var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
    var edits = new List<(int Start, int End, string Text)>();

    foreach (var property in syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>())
    {
        var accessors = property.AccessorList;

        if (accessors == null || accessors.Accessors.Any(a => a.Body != null || a.ExpressionBody != null))
        {
            continue;
        }

        var start = accessors.OpenBraceToken.GetPreviousToken().Span.End;
        // Preserve comments and directives rather than moving them across accessors.

        if (!string.IsNullOrWhiteSpace(source[start..accessors.SpanStart]) ||
            accessors.DescendantTrivia().Any(t => !string.IsNullOrWhiteSpace(t.ToString())))
        {
            continue;
        }

        edits.Add((start, accessors.Span.End,
            " { " + string.Join(" ", accessors.Accessors.Select(a => a.ToString().Trim())) + " }"));
    }

    foreach (var block in syntax.DescendantNodes().OfType<BlockSyntax>())
    {
        var span = block.GetLocation().GetLineSpan();

        if (block.Statements.Count == 0 || span.StartLinePosition.Line != span.EndLinePosition.Line ||
            block.Ancestors().OfType<BlockSyntax>().Any(a =>
                a.GetLocation().GetLineSpan().StartLinePosition.Line == a.GetLocation().GetLineSpan().EndLinePosition.Line))
        {
            continue;
        }

        var lineStart = source.LastIndexOf('\n', Math.Max(0, block.SpanStart - 1)) + 1;
        var indent = new string(source[lineStart..block.SpanStart].TakeWhile(c => c == ' ' || c == '\t').ToArray());
        var start = block.SpanStart;

        while (start > lineStart && (source[start - 1] == ' ' || source[start - 1] == '\t'))
        {
            start--;
        }

        var expanded = block.NormalizeWhitespace("    ", "\n").ToFullString();

        edits.Add((start, block.Span.End, (start > lineStart
            ? "\n" + indent
            : "") + expanded.Replace("\n", "\n" + indent)));
    }

    foreach (var edit in edits.OrderByDescending(e => e.Start))
    {
        source = source.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
    }

    syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
    edits.Clear();

    foreach (var block in syntax.DescendantNodes().OfType<BlockSyntax>().Where(b => b.Statements.Count == 0))
    {
        var start = block.OpenBraceToken.GetPreviousToken().Span.End;

        if (string.IsNullOrWhiteSpace(source[start..block.SpanStart]) &&
            block.DescendantTrivia().All(t => string.IsNullOrWhiteSpace(t.ToString())))
        {
            edits.Add((start, block.Span.End, " { }"));
        }
    }

    foreach (var edit in edits.OrderByDescending(e => e.Start))
    {
        source = source.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
    }

    syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
    edits.Clear();

    foreach (var expression in syntax.DescendantNodes().OfType<ConditionalExpressionSyntax>())
    {
        var ancestors = expression.Ancestors().OfType<ConditionalExpressionSyntax>().ToArray();
        var outer = ancestors.LastOrDefault() ?? expression;
        var lineStart = source.LastIndexOf('\n', Math.Max(0, outer.SpanStart - 1)) + 1;
        var indent = new string(source[lineStart..outer.SpanStart].TakeWhile(c => c == ' ' || c == '\t').ToArray())
            + new string(' ', 4 * (ancestors.Length + 1));

        foreach (var token in new[] { expression.QuestionToken, expression.ColonToken })
        {
            var previous = token.GetPreviousToken();
            var next = token.GetNextToken();

            if (string.IsNullOrWhiteSpace(source[previous.Span.End..token.SpanStart]))
            {
                edits.Add((previous.Span.End, token.SpanStart, "\n" + indent));
            }

            if (string.IsNullOrWhiteSpace(source[token.Span.End..next.SpanStart]))
            {
                edits.Add((token.Span.End, next.SpanStart, " "));
            }
        }
    }

    foreach (var edit in edits.OrderByDescending(e => e.Start))
    {
        source = source.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
    }

    syntax = CSharpSyntaxTree.ParseText(source).GetRoot();

    var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
    var insert = new HashSet<int>();
    var remove = new HashSet<int>();

    int Line(int pos) => syntax.SyntaxTree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(pos, 0)).StartLinePosition.Line;
    // Apply separation between sibling statements, not between parts of one construct
    // (else, catch, finally, or the trailing while in a do/while).

    var groups = syntax.DescendantNodes().OfType<BlockSyntax>().Select(b => b.Statements.AsEnumerable())
        .Concat(syntax.DescendantNodes().OfType<SwitchSectionSyntax>().Select(s => s.Statements.AsEnumerable()))
        .Append(((CompilationUnitSyntax)syntax).Members.OfType<GlobalStatementSyntax>().Select(g => g.Statement));

    foreach (var group in groups)
    {
        StatementSyntax? previous = null;

        foreach (var statement in group)
        {
            var startsBlock = statement is IfStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or TryStatementSyntax
                or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax or CheckedStatementSyntax
                or UnsafeStatementSyntax or BlockSyntax or LocalFunctionStatementSyntax;
            var last = previous?.GetLastToken() ?? default;

            if (last.IsKind(SyntaxKind.SemicolonToken))
            {
                last = last.GetPreviousToken();
            }

            var start = Line(statement.SpanStart);
            var declaration = statement is LocalDeclarationStatementSyntax;
            var previousDeclaration = previous is LocalDeclarationStatementSyntax;

            if (declaration && previousDeclaration)
            {
                var end = Line(previous!.Span.End - 1);

                if (lines.Skip(end + 1).Take(start - end - 1).All(string.IsNullOrWhiteSpace))
                {
                    for (var i = end + 1; i < start; i++)
                    {
                        remove.Add(i);
                    }
                }
            }
            else if (previous != null && (startsBlock || declaration != previousDeclaration || last.IsKind(SyntaxKind.CloseBraceToken) || previous is DoStatementSyntax) &&
                start > Line(previous.Span.End - 1) && !string.IsNullOrWhiteSpace(lines[start - 1]))
            {
                insert.Add(start);
            }

            previous = statement;
        }
    }

    foreach (var statement in syntax.DescendantNodes().OfType<ReturnStatementSyntax>())
    {
        var start = Line(statement.SpanStart);

        if (!lines[start].TrimStart().StartsWith("return", StringComparison.Ordinal))
        {
            continue;
        }

        var previous = start - 1;

        while (previous >= 0 && string.IsNullOrWhiteSpace(lines[previous]))
        {
            previous--;
        }

        if (previous < 0)
        {
            continue;
        }

        if (lines[previous].Trim() == "{")
        {
            for (var i = previous + 1; i < start; i++)
            {
                remove.Add(i);
            }
        }
        else if (previous == start - 1)
        {
            insert.Add(start);
        }
    }

    var output = new List<string>();

    for (var i = 0; i < lines.Count; i++)
    {
        if (insert.Contains(i) && !remove.Contains(i))
        {
            output.Add("");
        }

        if (!remove.Contains(i))
        {
            output.Add(lines[i]);
        }
    }

    var result = string.Join(original.Contains("\r\n")
        ? "\r\n"
        : "\n", output);
    var oldTokens = CSharpSyntaxTree.ParseText(original).GetRoot().DescendantTokens().Select(t => (t.RawKind, t.Text));
    var newTokens = CSharpSyntaxTree.ParseText(result).GetRoot().DescendantTokens().Select(t => (t.RawKind, t.Text));

    if (!oldTokens.SequenceEqual(newTokens))
    {
        throw new InvalidOperationException("Formatter changed C# tokens.");
    }

    return result;
}
