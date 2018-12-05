using ICSharpCode.Decompiler.CSharp.Syntax;

namespace BaGet.Decompiler.ASTVisitors
{
    internal class TypeContentRemovalVisitor : DepthFirstAstVisitor
    {
        public override void VisitMethodDeclaration(MethodDeclaration methodDeclaration) => methodDeclaration.Remove();
        public override void VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration) => constructorDeclaration.Remove();
        public override void VisitDestructorDeclaration(DestructorDeclaration destructorDeclaration) => destructorDeclaration.Remove();
        public override void VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration) => operatorDeclaration.Remove();
        public override void VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration) => propertyDeclaration.Remove();
        public override void VisitFieldDeclaration(FieldDeclaration fieldDeclaration) => fieldDeclaration.Remove();
        public override void VisitEventDeclaration(EventDeclaration eventDeclaration) => eventDeclaration.Remove();
        public override void VisitCustomEventDeclaration(CustomEventDeclaration eventDeclaration) => eventDeclaration.Remove();
        public override void VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration) => indexerDeclaration.Remove();
        public override void VisitUsingDeclaration(UsingDeclaration usingDeclaration) => usingDeclaration.Remove();
        public override void VisitBlockStatement(BlockStatement blockStatement) => blockStatement.Remove();
    }
}