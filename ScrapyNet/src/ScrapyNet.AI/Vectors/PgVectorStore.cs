using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapyNet.AI;

/// <summary>
/// <see cref="IVectorStore"/> on PostgreSQL with the <c>pgvector</c> extension, over plain ADO.NET: pass a
/// factory for any provider's connection (typically <c>() =&gt; new NpgsqlConnection(cs)</c>), so this
/// package carries no database driver. The table and an HNSW cosine index are created on first use.
/// </summary>
/// <remarks>
/// Vectors and metadata travel as text parameters cast to <c>vector</c> and <c>jsonb</c> in SQL, which every
/// driver supports. A <c>filter</c> passed to <see cref="SearchAsync"/> runs client-side over an
/// over-fetched candidate list, as with <see cref="QdrantVectorStore"/>.
/// </remarks>
public sealed partial class PgVectorStore : IVectorStore
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly string _table;
    private readonly int _dimensions;
    private bool _ensured;

    public PgVectorStore(Func<DbConnection> connectionFactory, string table, int dimensions)
    {
        if (!Identifier().IsMatch(table)) throw new ArgumentException($"'{table}' is not a valid table name (letters, digits, underscores; optionally schema-qualified).", nameof(table));
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        _connectionFactory = connectionFactory;
        _table = table;
        _dimensions = dimensions;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$")]
    private static partial Regex Identifier();

    private string IndexName => _table.Replace('.', '_') + "_embedding_hnsw";

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = _connectionFactory();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        if (!_ensured)
        {
            await ExecuteAsync(connection, ct, $"""
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE TABLE IF NOT EXISTS {_table} (
                    id text PRIMARY KEY,
                    embedding vector({_dimensions}) NOT NULL,
                    content text NOT NULL,
                    metadata jsonb NOT NULL DEFAULT '{"{}"}'
                );
                CREATE INDEX IF NOT EXISTS {IndexName} ON {_table} USING hnsw (embedding vector_cosine_ops);
                """).ConfigureAwait(false);
            _ensured = true;
        }
        return connection;
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in records)
        {
            if (record.Vector.Length != _dimensions)
                throw new ArgumentException($"Record {record.Id} has {record.Vector.Length} dimensions; the table expects {_dimensions}.");
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {_table} (id, embedding, content, metadata)
                VALUES (@id, CAST(@embedding AS vector), @content, CAST(@metadata AS jsonb))
                ON CONFLICT (id) DO UPDATE SET embedding = EXCLUDED.embedding, content = EXCLUDED.content, metadata = EXCLUDED.metadata
                """;
            Add(command, "id", record.Id);
            Add(command, "embedding", ToLiteral(record.Vector));
            Add(command, "content", record.Text);
            Add(command, "metadata", JsonSerializer.Serialize(record.Metadata, ScrapyJson.Default));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, content, metadata::text, 1 - (embedding <=> CAST(@query AS vector)) AS score
            FROM {_table}
            ORDER BY embedding <=> CAST(@query AS vector)
            LIMIT {(filter is null ? top : top * 4).ToString(CultureInfo.InvariantCulture)}
            """;
        Add(command, "query", ToLiteral(query));
        var matches = new List<VectorMatch>(top);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (matches.Count < top && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2), ScrapyJson.Default) ?? [];
            var record = new VectorRecord(reader.GetString(0), [], reader.GetString(1), metadata);
            if (filter is null || filter(record)) matches.Add(new VectorMatch(record, Convert.ToSingle(reader.GetValue(3), CultureInfo.InvariantCulture)));
        }
        return matches;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {_table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        if (list.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in list.Chunk(500))
        {
            await using var command = connection.CreateCommand();
            var names = batch.Select((id, i) =>
            {
                Add(command, "id" + i.ToString(CultureInfo.InvariantCulture), id);
                return "@id" + i.ToString(CultureInfo.InvariantCulture);
            }).ToList();
            command.CommandText = $"DELETE FROM {_table} WHERE id IN ({string.Join(", ", names)})";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>pgvector's text form: <c>[0.1,0.2,...]</c>, invariant culture, round-trippable floats.</summary>
    internal static string ToLiteral(float[] vector)
    {
        var sb = new StringBuilder(vector.Length * 10 + 2).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task ExecuteAsync(DbConnection connection, CancellationToken ct, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
