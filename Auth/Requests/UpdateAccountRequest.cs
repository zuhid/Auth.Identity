namespace Zuhid.Auth.Requests;

public record UpdateAccountRequest(
    string FirstName,
    string LastName,
    string Phone
);
