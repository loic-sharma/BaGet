using ICSharpCode.Decompiler.CSharp.Syntax;

namespace BaGet.Decompiler.ASTVisitors
{
    internal class PropertyContentRemovalVisitor : DepthFirstAstVisitor
    {
        public override void VisitAccessor(Accessor accessor) => accessor.Body = null;

        public override void VisitDocumentationReference(DocumentationReference documentationReference)
        {
            documentationReference.Remove();
        }
    }
}