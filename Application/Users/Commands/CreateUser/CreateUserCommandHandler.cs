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
    public class CreateUserCommandHandler(
        ILogger<CreateUserCommandHandler> logger, 
        IMapper mapper, 
        IUsersRepository usersRepository, 
        IFileUploadService fileUploadService, 
        IJWTService jWTService, 
        UserManager<User> userManager,
        ISearchIndexQueueService searchIndexQueue) : IRequestHandler<CreateUserCommand, CreateUserResponse>
    {

        public async Task<CreateUserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
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

                // Clean up uploaded profile photo if user creation fails
                if (!string.IsNullOrEmpty(userMapped.ProfilePhoto))
                {
                    await fileUploadService.DeleteImageAsync($"profile-images/{userMapped.UserName!}");
                }

                return new CreateUserResponse
                {
                    Result = new FollowUser.OperationResult
                    {
                        Message = $"User creation failed: {string.Join("; ", result.Errors.Select(e => e.Description))}",
                        Success = false
                    }
                };
            };
            
            var user = await userManager.FindByEmailAsync(request.Email);
            
            // Queue new user for Elasticsearch indexing
            if (user != null)
            {
                await searchIndexQueue.QueueUserIndexAsync(user.Id);
                logger.LogDebug("Queued new user {UserId} for search indexing", user.Id);
            }
            
            return new CreateUserResponse
            {
                Result = new FollowUser.OperationResult
                {
                    Message = "User creation succeed",
                    Success = true
                },
                AccessToken = jWTService.GenerateAccessToken(user!),
                ExpiresAt = DateTime.UtcNow.AddDays(60)
            };

        }
    }
}
