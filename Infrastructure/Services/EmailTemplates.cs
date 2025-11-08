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
}
