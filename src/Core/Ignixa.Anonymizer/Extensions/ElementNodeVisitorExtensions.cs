using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Visitors;

namespace Ignixa.Anonymizer.Extensions;

public static class ElementNodeVisitorExtensions
{
    public static void Accept(this IElement node, ResourceJsonNode resource, AbstractElementNodeVisitor visitor)
    {
        bool shouldVisitChild = visitor.Visit(resource, node);

        if (shouldVisitChild)
        {
            foreach (var child in node.Children())
            {
                child.Accept(resource, visitor);
            }
        }

        visitor.EndVisit(resource, node);
    }
}
