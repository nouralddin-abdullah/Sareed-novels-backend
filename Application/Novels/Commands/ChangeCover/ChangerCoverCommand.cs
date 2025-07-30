using System.Text.Json.Serialization;
using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Application.Novels.Commands.ChangeCover;

public class ChangerCoverCommand(Guid novelId, IFormFile coverImageUrl) : IRequest<OperationResult>
{

    public Guid NovelId { get; set; } = novelId;
    public IFormFile CoverImageUrl { get; set; } = coverImageUrl;
}
