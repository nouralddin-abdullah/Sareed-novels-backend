using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Chapters.Commands.UploadChapterImage;

/// <summary>A picture for a chapter of <see cref="NovelId"/>, uploaded by the novel's author (#86).</summary>
public record UploadChapterImageCommand(Guid NovelId, IFormFile Image) : IRequest<UploadChapterImageResult>;

/// <summary>The answer: the stored picture's absolute address, for a chapter's <c>&lt;img src&gt;</c>.</summary>
public record UploadChapterImageResult(string Url);
