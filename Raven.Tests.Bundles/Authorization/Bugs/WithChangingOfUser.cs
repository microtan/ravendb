extern alias client;
using System.Collections.Generic;

using Raven.Client.Exceptions;

using Xunit;

using System.Linq;

namespace Raven.Tests.Bundles.Authorization.Bugs
{
	public class WithChangingOfUser : AuthorizationTest
	{
		[Fact]
		public void BugWhenUpdatingUserRolesLoad()
		{
			var company = new Company
			{
				Name = "Hibernating Rhinos"
			};
            using (var s = store.OpenSession(DatabaseName))
			{
				s.Store(new client::Raven.Bundles.Authorization.Model.AuthorizationUser
				{
					Id = UserId,
					Name = "Ayende Rahien",
				});

				s.Store(company);

				client::Raven.Client.Authorization.AuthorizationClientExtensions.SetAuthorizationFor(s, company, new client::Raven.Bundles.Authorization.Model.DocumentAuthorization
				{
					Permissions =
						{
							new client::Raven.Bundles.Authorization.Model.DocumentPermission
							{
								Role = "Admins",
								Allow = true,
								Operation = "Company/Bid"
							}
						}
				});

				s.SaveChanges();
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				client::Raven.Client.Authorization.AuthorizationClientExtensions.SecureFor(s, UserId, "Company/Bid");

				Assert.Throws<ReadVetoException>(() => s.Load<Company>(company.Id));
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				var user = s.Load<client::Raven.Bundles.Authorization.Model.AuthorizationUser>(UserId);
				user.Roles = new List<string> {"Admins"};
				s.SaveChanges();
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				client::Raven.Client.Authorization.AuthorizationClientExtensions.SecureFor(s, UserId, "Company/Bid");

				s.Load<Company>(company.Id);
			}
		}

		[Fact]
		public void BugWhenUpdatingUserRolesQuery()
		{
			var company = new Company
			{
				Name = "Hibernating Rhinos"
			};
            using (var s = store.OpenSession(DatabaseName))
			{
				s.Store(new client::Raven.Bundles.Authorization.Model.AuthorizationUser
				{
					Id = UserId,
					Name = "Ayende Rahien",
				});

				s.Store(company);

				client::Raven.Client.Authorization.AuthorizationClientExtensions.SetAuthorizationFor(s, company, new client::Raven.Bundles.Authorization.Model.DocumentAuthorization
				{
					Permissions =
						{
							new client::Raven.Bundles.Authorization.Model.DocumentPermission
							{
								Role = "Admins",
								Allow = true,
								Operation = "Company/Bid"
							}
						}
				});

				s.SaveChanges();
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				client::Raven.Client.Authorization.AuthorizationClientExtensions.SecureFor(s, UserId, "Company/Bid");

				Assert.Empty(s.Query<Company>().ToArray());
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				var user = s.Load<client::Raven.Bundles.Authorization.Model.AuthorizationUser>(UserId);
				user.Roles = new List<string> { "Admins" };
				s.SaveChanges();
			}

            using (var s = store.OpenSession(DatabaseName))
			{
				client::Raven.Client.Authorization.AuthorizationClientExtensions.SecureFor(s, UserId, "Company/Bid");

				Assert.NotEmpty(s.Query<Company>().ToArray());
		
			}
		}
	}
}