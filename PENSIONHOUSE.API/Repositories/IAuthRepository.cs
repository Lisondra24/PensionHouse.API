public interface IAuthRepository
{
    // SP_NORBERTODB_LOGIN
    Task<IEnumerable<Account>> GetLoginAccountsAsync(string email, string loginType);

    // SP_NORBERTODB_CREATE_ACCOUNT
    Task<int> CreateAccountAsync(string role, string fullName, string email, string passwordHash, string? contactNo);
}