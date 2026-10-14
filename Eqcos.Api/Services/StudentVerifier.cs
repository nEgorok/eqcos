using Eqcos.Api.Models;

namespace Eqcos.Api.Services;

// кто решает, подтверждать ли нового студента
public interface IStudentVerifier
{
    // вернуть "Approved" (подтвердить сразу) или "Pending" (ждать админа)
    string Check(User user);
}

// сейчас: всё вручную, каждого нового студента проверяет админ
public class ManualVerifier : IStudentVerifier
{
    public string Check(User user) => "Pending";
}

// ПОТОМ можно сделать так (когда техникум даст список студентов):
// public class CollegeListVerifier : IStudentVerifier
// {
//     public string Check(User user) =>
//         номер, ФИО и группа есть в списке техникума ? "Approved" : "Pending";
// }