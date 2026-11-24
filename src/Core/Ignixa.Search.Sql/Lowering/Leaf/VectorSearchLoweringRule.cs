using System.Globalization;
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a prepared <see cref="VectorSearchExpression"/> to its gate, a <see cref="CteDefinition.VectorMatchSource"/>.
/// Where the leaf may sit in the tree, and the ranking that reads the gate, are structural concerns owned by
/// <see cref="StructuralContext.LowerVectorSearch"/> and <see cref="Ignixa.Search.Sql.Lowering.Lower"/>.
/// </summary>
internal static class VectorSearchLoweringRule
{
    /// <summary>
    /// Returns the gate, or — when <c>dbo.EmbeddingModel</c> has no row for the query's model, so no vector was
    /// ever written under it — the compiler's known-miss source: a base set filtered by
    /// <see cref="Predicate.False"/>, which matches nothing and is reported as a known miss in diagnostics.
    /// </summary>
    /// <exception cref="NotSupportedException">The expression was never embedded, or its embedding or
    /// threshold cannot be bound to the schema's vector column.</exception>
    public static CteDefinition Lower(VectorSearchExpression expression, LeafContext context, short? resourceTypeId)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var prepared = expression.Prepared
            ?? throw new NotSupportedException(
                $"Semantic query was not prepared: '{expression.Parameter.Code}' reached the SQL compiler without " +
                "an embedding. The caller must embed the query text (VectorSearchExpression.WithPrepared) before " +
                "compiling; compiling it without one would have nothing to measure distance against.");

        if (prepared.Embedding.Length != VectorSearchEmitter.Dimensions)
        {
            throw new NotSupportedException(
                $"The prepared embedding for '{expression.Parameter.Code}' has {prepared.Embedding.Length} " +
                $"dimensions, but dbo.VectorSearchParam stores vector({VectorSearchEmitter.Dimensions}). Casting it " +
                "would fail at execution with an opaque conversion error, so it is reported here instead.");
        }

        // NaN compares false against every distance, so it would silently return nothing; infinities and
        // negatives are never a threshold a minimumScore in [0, 1] can produce.
        if (!double.IsFinite(prepared.MaxDistance) || prepared.MaxDistance < 0)
        {
            throw new NotSupportedException(
                $"The prepared maximum distance for '{expression.Parameter.Code}' is " +
                $"{prepared.MaxDistance.ToString(CultureInfo.InvariantCulture)}; a cosine-distance threshold must " +
                "be a finite, non-negative number.");
        }

        var searchParamId = context.SearchParamId(expression.Parameter);

        if (context.EmbeddingModelId(prepared.EmbeddingModelKey) is not { } embeddingModelId)
        {
            var miss = new Predicate.False(
                $"No vectors exist for embedding model '{prepared.EmbeddingModelKey}', so semantic search on " +
                $"'{expression.Parameter.Code}' cannot match.");

            return resourceTypeId is { } typeId
                ? new CteDefinition.ResourceSource(typeId, miss)
                : CteDefinition.MultiTypeResourceSource.AllTypes(miss);
        }

        return new CteDefinition.VectorMatchSource(
            resourceTypeId,
            searchParamId,
            embeddingModelId,
            context.Parameter(SqlVectorText.Format(prepared.Embedding.Span)),
            context.Parameter(prepared.MaxDistance));
    }
}
