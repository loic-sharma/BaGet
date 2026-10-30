import { Link, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { AdminUsers } from './pages/AdminUsers';
import { ConfirmEmail } from './pages/ConfirmEmail';
import { ForgotPassword } from './pages/ForgotPassword';
import { Home } from './pages/Home';
import { PackageDetail } from './pages/PackageDetail';
import { Register } from './pages/Register';
import { ResetPassword } from './pages/ResetPassword';
import { SignIn } from './pages/SignIn';

export function App() {
  const auth = useAuth();
  if (!auth.ready) return <p>Loading...</p>;
  return (
    <>
      <header>
        <Link to="/" className="brand">BaGet</Link>
        <nav>
          <Link to="/">Packages</Link>
          <Link to="/admin/users">Users</Link>
        </nav>
        <span className="spacer" />
        {auth.signedIn ? (
          <>
            <span>{auth.userName}</span>
            <button onClick={() => void auth.signOut()}>Sign out</button>
            <button onClick={() => void auth.signOut(true)} title="Ends every session of this account">Sign out everywhere</button>
          </>
        ) : (
          <Link to="/sign-in">Sign in</Link>
        )}
      </header>
      <main>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/packages/:id" element={<PackageDetail />} />
          <Route path="/packages/:id/:version" element={<PackageDetail />} />
          <Route path="/sign-in" element={<SignIn />} />
          <Route path="/register" element={<Register />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/reset-password" element={<ResetPassword />} />
          <Route path="/confirm-email" element={<ConfirmEmail />} />
          <Route path="/admin/users" element={<AdminUsers />} />
          <Route path="*" element={<p>Page not found. <Link to="/">Home</Link></p>} />
        </Routes>
      </main>
    </>
  );
}
