using System.ComponentModel.DataAnnotations;

namespace AuthAPI.Dtos
{
    public class DeleteAccountDto
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirmation is required.")]
        public bool ConfirmDeletion { get; set; }
    }
}