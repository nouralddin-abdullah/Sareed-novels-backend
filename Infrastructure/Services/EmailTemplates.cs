namespace Infrastructure.Services;

public static class EmailTemplates
{
    public static string GetConfirmEmailTemplate(string confirmationLink)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Confirm Your Email - Sard</title>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{
            background: #f7f7f7;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 0;
        }}
        .container {{
            max-width: 480px;
            margin: 40px auto;
            background: #fff;
            border-radius: 12px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.07);
            padding: 32px 24px;
        }}
        .logo {{
            text-align: center;
            font-size: 2rem;
            color: #2d6cdf;
            font-weight: bold;
            margin-bottom: 16px;
            letter-spacing: 2px;
        }}
        .title {{
            font-size: 1.3rem;
            color: #222;
            margin-bottom: 12px;
            text-align: center;
        }}
        .message {{
            font-size: 1rem;
            color: #444;
            margin-bottom: 24px;
            text-align: center;
        }}
        .button {{
            display: block;
            width: 100%;
            background: linear-gradient(90deg, #2d6cdf 0%, #4e9cff 100%);
            color: #fff;
            text-decoration: none;
            padding: 14px 0;
            border-radius: 6px;
            font-size: 1.1rem;
            font-weight: bold;
            text-align: center;
            margin-bottom: 16px;
            transition: background 0.2s;
        }}
        .button:hover {{
            background: linear-gradient(90deg, #1a4fa0 0%, #3577c9 100%);
        }}
        .footer {{
            font-size: 0.9rem;
            color: #888;
            text-align: center;
            margin-top: 24px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""logo"">Sard Novels</div>
        <div class=""title"">Confirm Your Email</div>
        <div class=""message"">
            Thank you for joining <b>Sard</b>!<br>
            To activate your account, please click the button below to confirm your email address.
        </div>
        <a href=""{confirmationLink}"" class=""button"">Confirm Email Address</a>
        <div class=""footer"">
            If you didn't create this account, you can safely ignore this message.<br>
            Sard Team
        </div>
    </div>
</body>
</html>";
    }

    public static string GetResetPasswordTemplate(string resetPasswordLink)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Reset Your Password - Sard</title>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{
            background: #f7f7f7;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 0;
        }}
        .container {{
            max-width: 480px;
            margin: 40px auto;
            background: #fff;
            border-radius: 12px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.07);
            padding: 32px 24px;
        }}
        .logo {{
            text-align: center;
            font-size: 2rem;
            color: #2d6cdf;
            font-weight: bold;
            margin-bottom: 16px;
            letter-spacing: 2px;
        }}
        .title {{
            font-size: 1.3rem;
            color: #222;
            margin-bottom: 12px;
            text-align: center;
        }}
        .message {{
            font-size: 1rem;
            color: #444;
            margin-bottom: 24px;
            text-align: center;
        }}
        .button {{
            display: block;
            width: 100%;
            background: linear-gradient(90deg, #2d6cdf 0%, #4e9cff 100%);
            color: #fff;
            text-decoration: none;
            padding: 14px 0;
            border-radius: 6px;
            font-size: 1.1rem;
            font-weight: bold;
            text-align: center;
            margin-bottom: 16px;
            transition: background 0.2s;
        }}
        .button:hover {{
            background: linear-gradient(90deg, #1a4fa0 0%, #3577c9 100%);
        }}
        .footer {{
            font-size: 0.9rem;
            color: #888;
            text-align: center;
            margin-top: 24px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""logo"">Sard Novels</div>
        <div class=""title"">Reset Your Password</div>
        <div class=""message"">
            A password reset was requested for your account at <b>Sard</b>.<br>
            To continue, please click the button below to create a new password.
        </div>
        <a href=""{resetPasswordLink}"" class=""button"">Reset Password</a>
        <div class=""footer"">
            If you didn't request a password reset, you can safely ignore this message.<br>
            Sard Team
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Sent when a Google sign-in takes over an account whose email address was never verified (someone may have
    /// registered it with the owner's address): the password on it was removed and every other session ended.
    /// </summary>
    public static string GetPasswordRemovedTemplate()
    {
        return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<head>
    <meta charset=""UTF-8"">
    <title>تمت إزالة كلمة المرور من حسابك - سرد</title>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{
            background: #f7f7f7;
            font-family: Tahoma, 'Segoe UI', Arial, sans-serif;
            margin: 0;
            padding: 0;
        }}
        .container {{
            max-width: 480px;
            margin: 40px auto;
            background: #fff;
            border-radius: 12px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.07);
            padding: 32px 24px;
            text-align: right;
        }}
        .logo {{
            text-align: center;
            font-size: 2rem;
            color: #2d6cdf;
            font-weight: bold;
            margin-bottom: 16px;
        }}
        .title {{
            font-size: 1.3rem;
            color: #222;
            margin-bottom: 12px;
            text-align: center;
        }}
        .message {{
            font-size: 1rem;
            color: #444;
            line-height: 1.8;
            margin-bottom: 24px;
        }}
        .button {{
            display: block;
            width: 100%;
            background: linear-gradient(90deg, #2d6cdf 0%, #4e9cff 100%);
            color: #fff;
            text-decoration: none;
            padding: 14px 0;
            border-radius: 6px;
            font-size: 1.1rem;
            font-weight: bold;
            text-align: center;
            margin-bottom: 16px;
        }}
        .footer {{
            font-size: 0.9rem;
            color: #888;
            text-align: center;
            margin-top: 24px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""logo"">سرد</div>
        <div class=""title"">تمت إزالة كلمة المرور من حسابك</div>
        <div class=""message"">
            سجّلت الدخول إلى <b>سرد</b> بحساب Google الذي يملك بريدك الإلكتروني.<br>
            لم يكن هذا البريد مؤكَّدًا على حسابك في سرد، لذلك أزلنا كلمة المرور التي كانت عليه وأنهينا كل جلسات الدخول الأخرى، حتى لا يستطيع من أنشأ الحساب ببريدك (إن لم تكن أنت) الدخول إليه.<br>
            يمكنك متابعة الدخول بحساب Google، أو تعيين كلمة مرور جديدة من صفحة «نسيت كلمة المرور».
        </div>
        <a href=""https://www.sardnovels.com/forgot-password"" class=""button"">تعيين كلمة مرور جديدة</a>
        <div class=""footer"">
            إن كنت أنت من أنشأ الحساب بكلمة المرور فلا داعي للقلق، فقط عيّن كلمة مرور جديدة إن أردت الدخول بها.<br>
            فريق سرد
        </div>
    </div>
</body>
</html>";
    }
}
