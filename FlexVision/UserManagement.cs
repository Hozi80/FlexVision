using System;

namespace FlexVision
{
    public enum UserRole { Operator, Technician, Administrator }

    public class UserSession
    {
        private static UserSession _instance;
        public static UserSession Instance
        {
            get
            {
                if (_instance == null) _instance = new UserSession();
                return _instance;
            }
        }

        public UserRole CurrentRole { get; private set; } = UserRole.Operator; // Alapértelmezett indítás
        public string CurrentUserName { get; private set; } = "Gepkezelo_01";

        public event EventHandler UserChanged;

        private UserSession() { }

        public void Login(string username, UserRole role)
        {
            CurrentUserName = username;
            CurrentRole = role;
            UserChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool CanEditPipeline()
        {
            // Csak Technikus és Adminisztrátor nyúlhat a Setup módhoz!
            return CurrentRole == UserRole.Technician || CurrentRole == UserRole.Administrator;
        }
    }
}
