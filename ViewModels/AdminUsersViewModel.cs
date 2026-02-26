namespace SmartELibrary.ViewModels;

public class AdminUsersViewModel
{
    public string RoleFilter { get; set; } = "All";

    public IReadOnlyList<AdminUserRowViewModel> Users { get; set; } = Array.Empty<AdminUserRowViewModel>();
}
