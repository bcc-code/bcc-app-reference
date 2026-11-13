using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Changes;

/// <summary>
/// The alias a person acts under in <see cref="Change"/>. Changes point to the alias, not the person uid, so erasing a
/// person clears <see cref="PersonUid"/> here and their changes stay, linked to an alias that points to no one.
/// </summary>
public sealed class Actor
{
    private Actor()
    {
    }

    public Guid Id { get; private set; }

    public Guid? PersonUid { get; private set; }

    /// <summary>The person's alias, created on their first change. Two first changes at once get the same alias.</summary>
    public static async Task<Guid> AliasForAsync(TemplateDb db, Guid personUid, CancellationToken cancellationToken)
    {
        var existing = await Find(db, personUid, cancellationToken);
        if (existing is { } id) return id;

        var table = db.TableOf<Actor>();
#pragma warning disable EF1002 // The table name comes from the model, not from input; the values are parameters.
        await db.Database.ExecuteSqlRawAsync(
            $"insert into {table} (id, person_uid) values ({{0}}, {{1}}) on conflict (person_uid) do nothing",
            [Guid.CreateVersion7(), personUid],
            cancellationToken);
#pragma warning restore EF1002
        return await Find(db, personUid, cancellationToken)
            ?? throw new InvalidOperationException("The actor alias was not stored.");
    }

    private static Task<Guid?> Find(TemplateDb db, Guid personUid, CancellationToken cancellationToken)
    {
        return db.Actors.Where(a => a.PersonUid == personUid).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(cancellationToken);
    }
}
