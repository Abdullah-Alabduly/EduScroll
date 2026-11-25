class LoginRequestModel {
  const LoginRequestModel({
    required this.email,
    required this.password,
  });

  final String email;
  final String password;

  Map<String, dynamic> toJson() => {
        'email': email.trim(),
        'password': password,
      };

  @override
  String toString() => 'LoginRequestModel(password: [REDACTED])';
}
