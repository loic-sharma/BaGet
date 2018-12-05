using BaGet.Decompiler.ASTVisitors;
using ICSharpCode.Decompiler.CSharp.Syntax;

namespace BaGet.Decompiler.Decompilation
{
    internal static class DeclarationRenderer
    {
        public static string RenderTypeDeclaration(SyntaxTree typeAst)
        {
            AstNode astCopy = typeAst.Clone();

            astCopy.AcceptVisitor(new TypeContentRemovalVisitor());
            string declaration = astCopy.ToString().Replace("\r", "").Replace("{", "").Replace("}", "").Trim();

            return declaration;
        }

        public static string RenderMethodDeclaration(SyntaxTree typeAst)
        {
            AstNode astCopy = typeAst.Clone();

            astCopy.AcceptVisitor(new MethodContentRemovalVisitor());
            string declaration = astCopy.ToString().Replace("\r", "").Replace("{", "").Replace("}", "").Trim();

            return declaration;
        }

        public static string RenderPropertyDeclaration(SyntaxTree typeAst)
        {
            AstNode astCopy = typeAst.Clone();

            astCopy.AcceptVisitor(new PropertyContentRemovalVisitor());
            string declaration = astCopy.ToString().Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();

            return declaration;
        }
    }
}