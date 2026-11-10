using System;
using System.Collections.Generic;
using System.Linq;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Security
{
    public class SecurityRulesTests
    {
        // --- hesla -------------------------------------------------------------------------------------

        [Theory]
        [InlineData("12345678")]
        [InlineData("Žluťoučký kůň")]
        [InlineData("administrator")]
        [InlineData("        ")]   // osm mezer je pořád osm znaků
        public void Passwords_of_at_least_eight_characters_are_accepted(string password)
        {
            Assert.Null(PasswordPolicy.Validate(password));
        }

        [Theory]
        [InlineData(null, "Zadejte heslo.")]
        [InlineData("", "Zadejte heslo.")]
        [InlineData("1234567", "Heslo musí mít alespoň 8 znaků.")]
        [InlineData("admin", "Heslo nesmí být „admin“.")]
        [InlineData("Admin", "Heslo nesmí být „admin“.")]
        public void Weak_passwords_are_refused_with_a_reason(string password, string expectedError)
        {
            Assert.Equal(expectedError, PasswordPolicy.Validate(password));
        }

        // --- záchranný kód -------------------------------------------------------------------------------

        [Fact]
        public void Recovery_code_has_24_unambiguous_characters_in_groups_of_four()
        {
            var code = RecoveryCode.Generate();

            Assert.Equal(29, code.Length);
            Assert.Matches("^([A-HJ-NP-Z2-9]{4}-){5}[A-HJ-NP-Z2-9]{4}$", code);
            Assert.DoesNotContain("I", code);
            Assert.DoesNotContain("O", code);
            Assert.DoesNotContain("0", code);
            Assert.DoesNotContain("1", code);
        }

        [Fact]
        public void Recovery_alphabet_has_32_distinct_characters_without_look_alikes()
        {
            Assert.Equal(32, RecoveryCode.Alphabet.Length);
            Assert.Equal(32, RecoveryCode.Alphabet.Distinct().Count());
            Assert.DoesNotContain(RecoveryCode.Alphabet, character => "IO01".IndexOf(character) >= 0);
        }

        [Fact]
        public void Recovery_codes_do_not_repeat_and_use_the_whole_alphabet()
        {
            var codes = new HashSet<string>();
            var seen = new HashSet<char>();

            for (var i = 0; i < 300; i++)
            {
                var code = RecoveryCode.Generate();
                Assert.True(codes.Add(code));
                foreach (var character in code.Where(character => character != '-'))
                {
                    seen.Add(character);
                }
            }

            Assert.Equal(32, seen.Count);
        }

        [Theory]
        [InlineData("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", "ABCDEFGHJKLMNPQRSTUVWXYZ")]
        [InlineData("abcd-efgh-jklm-npqr-stuv-wxyz", "ABCDEFGHJKLMNPQRSTUVWXYZ")]
        [InlineData("  ABCD EFGH JKLM NPQR STUV 2345 ", "ABCDEFGHJKLMNPQRSTUV2345")]
        [InlineData("abcdefghjklmnpqrstuv6789", "ABCDEFGHJKLMNPQRSTUV6789")]
        public void Typed_recovery_code_is_normalized(string typed, string expected)
        {
            Assert.Equal(expected, RecoveryCode.Normalize(typed));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ABCD-EFGH")]                                // krátký
        [InlineData("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345")]       // dlouhý
        [InlineData("ABCD-EFGH-JKLM-NPQR-STUV-WXY0")]            // nula v abecedě není
        [InlineData("ABCD-EFGH-IKLM-NPQR-STUV-WXYZ")]            // I v abecedě není
        [InlineData("ABCD_EFGH_JKLM_NPQR_STUV_WXYZ")]
        public void Malformed_recovery_code_is_refused(string typed)
        {
            Assert.Null(RecoveryCode.Normalize(typed));
        }

        [Fact]
        public void Formatting_and_normalizing_are_inverse()
        {
            var code = RecoveryCode.Generate();

            Assert.Equal(code, RecoveryCode.Format(RecoveryCode.Normalize(code)));
        }

        // --- blokace přihlášení ----------------------------------------------------------------------------

        [Fact]
        public void Throttle_locks_after_the_fifth_failure_for_five_minutes()
        {
            var clock = new FakeClock();
            var throttle = new LoginThrottle(clock);

            for (var attempt = 1; attempt <= 4; attempt++)
            {
                Assert.False(throttle.RegisterFailure());
                Assert.Null(throttle.GetRemainingLockout());
                Assert.Equal(attempt, throttle.FailedAttempts);
            }

            Assert.True(throttle.RegisterFailure());
            Assert.Equal(TimeSpan.FromMinutes(5), throttle.GetRemainingLockout());

            clock.Advance(TimeSpan.FromSeconds(299));
            Assert.Equal(TimeSpan.FromSeconds(1), throttle.GetRemainingLockout());

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Null(throttle.GetRemainingLockout());
            Assert.Equal(0, throttle.FailedAttempts);   // po blokaci je k dispozici znovu pět pokusů
        }

        [Fact]
        public void Throttle_starts_counting_again_after_a_success()
        {
            var throttle = new LoginThrottle(new FakeClock());

            for (var attempt = 0; attempt < 4; attempt++)
            {
                throttle.RegisterFailure();
            }

            throttle.RegisterSuccess();

            Assert.Equal(0, throttle.FailedAttempts);
            Assert.False(throttle.RegisterFailure());
            Assert.Null(throttle.GetRemainingLockout());
        }

        // --- e-maily -----------------------------------------------------------------------------------------

        [Theory]
        [InlineData("jana.novakova@example.cz", true)]
        [InlineData("  Admin@Admin.CZ ", true)]
        [InlineData("a@b.c", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("bez-zavinace.cz", false)]
        [InlineData("dva@@example.cz", false)]
        [InlineData("s mezerou@example.cz", false)]
        [InlineData("bez-tecky@example", false)]
        public void Email_shape_is_checked(string email, bool expected)
        {
            Assert.Equal(expected, EmailAddress.IsValid(email));
        }

        [Fact]
        public void Email_longer_than_256_characters_is_invalid()
        {
            var tooLong = new string('a', 250) + "@example.cz";

            Assert.False(EmailAddress.IsValid(tooLong));
        }
    }
}
