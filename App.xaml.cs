using RadioApp.Data;
using System.Data.Entity;
using System.Windows;

namespace RadioApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Database.SetInitializer<RadioDbContext>(null);

            base.OnStartup(e);
        }
    }
}