using ICSharpCode.Decompiler.CSharp.Syntax;

namespace BaGet.Decompiler.ASTVisitors
{
    internal class MethodContentRemovalVisitor : DepthFirstAstVisitor
    {
        public override void VisitUsingDeclaration(UsingDeclaration usingDeclaration) => usingDeclaration.Remove();
        public override void VisitMethodDeclaration(MethodDeclaration methodDeclaration) => methodDeclaration.Body = null;
    }
}