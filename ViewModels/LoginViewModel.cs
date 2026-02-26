using System.ComponentModel.DataAnnotations;

namespace SmartELibrary.ViewModels;

public class LoginViewModel
{
    [Required, StringLength(15)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
