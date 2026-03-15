namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

public record CreateUserRequest(string Name, string Email);
public record CreateUserResponse(int Id, string Name, string Email);
public record GetUserRequest(int Id);
public record UserResponse(int Id, string Name, string Email);
public record UpdateUserRequest(int Id, string Name, string Email);
