using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zuhid.Auth.Entities;
using Zuhid.Auth.Repositories;
using Zuhid.Auth.Requests;

namespace Zuhid.Auth.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Roles = "Admin")]
public class UserController(UserRepository userRepository) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var users = await userRepository.GetAll();
        var active = users.Count(u => u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.UtcNow);
        return Ok(new { active, inactive = users.Count - active });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await userRepository.GetAll();
        return Ok(users.Select(u => new
        {
            u.Id,
            u.FirstName,
            u.LastName,
            u.Email,
            u.PhoneNumber,
            IsActive = u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.UtcNow
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest request)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.Phone
        };

        var (createdUser, errors) = await userRepository.AdminCreate(user, request.Password);
        if (errors != null)
        {
            errors.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
            return BadRequest(ModelState);
        }

        return Ok(new
        {
            createdUser!.Id,
            createdUser.FirstName,
            createdUser.LastName,
            createdUser.Email,
            createdUser.PhoneNumber,
            IsActive = true
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminUpdateUserRequest request)
    {
        var errors = await userRepository.AdminUpdate(id, request.FirstName, request.LastName, request.Phone);
        if (errors != null)
        {
            errors.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
            return BadRequest(ModelState);
        }
        return Ok();
    }

    [HttpPost("{id:guid}/Deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var errors = await userRepository.SetActive(id, false);
        if (errors != null)
        {
            errors.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
            return BadRequest(ModelState);
        }
        return Ok();
    }

    [HttpPost("{id:guid}/Activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var errors = await userRepository.SetActive(id, true);
        if (errors != null)
        {
            errors.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
            return BadRequest(ModelState);
        }
        return Ok();
    }

    private Guid UserId
    {
        get
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.Parse(userId!);
        }
    }
}
