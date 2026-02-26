using System.ComponentModel.DataAnnotations;

namespace SmartELibrary.Models;

public class Student
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [Required, StringLength(50)]
    public string EnrollmentNumber { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
