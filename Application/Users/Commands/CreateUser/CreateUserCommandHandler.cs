using Application.Services;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.CreateUser
{
    public class CreateUserCommandHandler(ILogger<CreateUserCommandHandler> logger, IMapper mapper, IUsersRepository usersRepository, 
        IFileUploadService fileUploadService) : IRequestHandler<CreateUserCommand, IdentityResult>
    {

        public async Task<IdentityResult> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        {
            logger.LogInformation("Creating a new user {@user}", request);
            var userMapped = mapper.Map<User>(request);
            userMapped.CreatedAt = DateTime.UtcNow;
            if (request.ProfilePhoto != null)
            {
                using var stream = request.ProfilePhoto.OpenReadStream();
                userMapped.ProfilePhoto = await fileUploadService.UploadImageAsync(
                    stream,
                    request.ProfilePhoto.FileName,
                    request.ProfilePhoto.ContentType,
                    request.UserName
                    );
            }
            var result = await usersRepository.Create(userMapped, request.Password);
            if (!result.Succeeded)
            {
                logger.LogWarning("User creation failed for {Email}: {Errors}",
                    request.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                await fileUploadService.DeleteImageAsync($"profile-images/{userMapped.UserName!}");
            }
            return result;
        }
    }
}
