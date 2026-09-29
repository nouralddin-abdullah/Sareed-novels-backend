using MediatR;

namespace Application.Library.Commands.RemoveFromLibrary;

/// <summary>
/// DELETE /api/library/novel/{novelId} (#33): «إزالة من المكتبة». Deletes the caller's progress entry for the novel,
/// so it leaves her library and its new chapters stop notifying her; nothing else of hers changes. Reading the novel
/// again adds it back (track-progress), with notifications on. True when an entry was removed, false when there was
/// none: the state was already as asked, which the endpoint answers 204 too (like the idempotent writes since #25).
/// </summary>
public class RemoveFromLibraryCommand(Guid novelId) : IRequest<bool>
{
    public Guid NovelId { get; } = novelId;
}
