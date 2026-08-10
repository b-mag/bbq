import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'CARCOSA - The King in Yellow',
  description: 'A cooperative top-down RPG set in the world of the King in Yellow',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
