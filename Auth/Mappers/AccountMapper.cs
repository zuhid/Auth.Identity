using Zuhid.Auth.Entities;
using Zuhid.Auth.Requests;

namespace Zuhid.Auth.Mappers;

public class AccountMapper
{
    public User Map(RegisterRequest request) => new()
    {
        FirstName = request.FirstName,
        LastName = request.LastName,
        UserName = request.Email,
        Email = request.Email,
        PhoneNumber = request.Phone
    };
}
