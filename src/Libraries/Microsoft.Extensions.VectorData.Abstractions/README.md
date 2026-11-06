# Microsoft.Extensions.VectorData.Abstractions

This package is supported on .NET Framework 4.6.2 and later.

This package contains abstract classes and utilities for accessing vector databases. Actual implementations are provided separately in other packages; see <https://learn.microsoft.com/dotnet/ai/vector-stores/overview> for more information.

The abstractions in this package expose functionality for:

- Mapping .NET types to a collection (e.g. table) in a vector database, with arbitrary schema support.
- Creating, listing and deleting collections in the database.
- Creating, retrieving, updating and deleting records.
- Similarity search using vector embeddings.
- Filtering records using LINQ filters.
- Hybrid search combining vector similarity and keyword search.
- Built-in embedding generation using `Microsoft.Extensions.AI`.

## How to Use

This package typically isn't referenced directly by application; it's usually a transitive dependency of a provider.

## Main Types

The main types provided by this library are:

- [Microsoft.Extensions.VectorData.VectorStore](https://learn.microsoft.com/dotnet/api/microsoft.extensions.vectordata.vectorstore)
- [Microsoft.Extensions.VectorData.VectorStoreCollection](https://learn.microsoft.com/dotnet/api/microsoft.extensions.vectordata.vectorstorecollection-2)

## Additional Documentation

- [Conceptual documentation](https://learn.microsoft.com/dotnet/ai/vector-stores/overview)

## Feedback & Contributing

Microsoft.Extensions.VectorData.Abstractions is released as open source under the [MIT license](https://licenses.nuget.org/MIT). Bug reports and contributions are welcome at [the GitHub repository](https://github.com/dotnet/extensions).
