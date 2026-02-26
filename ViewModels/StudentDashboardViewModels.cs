namespace SmartELibrary.ViewModels;

public class StudentDashboardViewModel
{
    public List<StudentEnrolledSubjectRowViewModel> Subjects { get; set; } = new();
}

public class StudentEnrolledSubjectRowViewModel
{
    public string SemesterName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}
