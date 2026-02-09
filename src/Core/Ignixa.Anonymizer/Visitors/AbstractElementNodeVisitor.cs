using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer.Visitors;

public abstract class AbstractElementNodeVisitor
{
    public virtual bool Visit(ResourceJsonNode resource, IElement node)
    {
        return true;
    }

    public virtual void EndVisit(ResourceJsonNode resource, IElement node)
    {
    }
}
