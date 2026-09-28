using Application.Common;
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
        UserManager<User> userManager) : IRequestHandler<CreateUserCommand, CreateUserResponse>
    {

        public async Task<CreateUserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        {
            logger.LogInformation("Creating a new user {UserName}", request.UserName);
            var userMapped = mapper.Map<User>(request);
            userMapped.CreatedAt = DateTime.UtcNow;
            if (request.ProfilePhoto != null)
            {
                using var stream = request.ProfilePhoto.OpenReadStream();
                userMapped.ProfilePhoto = await fileUploadService.UploadImageAsync(
                    stream,
                    request.ProfilePhoto.FileName,
                    request.ProfilePhoto.ContentType,
                    userMapped.Id
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
                    await fileUploadService.DeleteImageAsync(userMapped.ProfilePhoto);
                }

                // ASP.NET Identity's code for the first problem: DuplicateUserName, DuplicateEmail, InvalidUserName...
                var code = result.Errors.FirstOrDefault()?.Code ?? "OperationFailed";
                var message = ArabicText.Sentences(["تعذّر إنشاء الحساب", .. result.Errors.Select(e => e.Description)]);
                return new CreateUserResponse
                {
                    Result = new FollowUser.OperationResult
                    {
                        Code = code,
                        Message = message,
                        Success = false
                    },
                    Code = code,
                    Message = message,
                    Errors = result.Errors.ToList()
                };
            };
            
            var user = await userManager.FindByEmailAsync(request.Email);
            
            
            return new CreateUserResponse
            {
                Result = new FollowUser.OperationResult
                {
                    Message = "تم إنشاء حسابك. أهلًا بك في سرد!",
                    Success = true
                },
                AccessToken = jWTService.GenerateAccessToken(user!),
                ExpiresAt = DateTime.UtcNow.AddDays(60)
            };

        }
    }
}
