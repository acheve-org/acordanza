import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../test/renderWithProviders';
import { MainNav } from './MainNav';

describe('MainNav', () => {
  it('renders all four section links with their correct targets', () => {
    renderWithProviders(<MainNav />);
    expect(screen.getByRole('link', { name: 'Busca' })).toHaveAttribute('href', '/routes');
    expect(screen.getByRole('link', { name: 'Explora' })).toHaveAttribute('href', '/explore');
    expect(screen.getByRole('link', { name: 'Sigue' })).toHaveAttribute('href', '/fa');
    expect(screen.getByRole('link', { name: 'Mi acordanza' })).toHaveAttribute('href', '/my-acordanza');
  });

  it('exposes aria-expanded/aria-controls and toggles on click', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MainNav />);
    const trigger = screen.getByRole('button', { name: 'Menú' });
    const items = screen.getByRole('link', { name: 'Busca' }).closest('#main-nav-items') as HTMLElement;

    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(trigger).toHaveAttribute('aria-controls', 'main-nav-items');
    expect(items).not.toHaveClass('is-open');

    await user.click(trigger);

    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(items).toHaveClass('is-open');
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MainNav />);
    const trigger = screen.getByRole('button', { name: 'Menú' });

    await user.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');

    await user.keyboard('{Escape}');

    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(trigger).toHaveFocus();
  });

  it('closes when navigating to another section', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MainNav />, { initialEntries: ['/routes'] });
    const trigger = screen.getByRole('button', { name: 'Menú' });

    await user.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');

    await user.click(screen.getByRole('link', { name: 'Explora' }));

    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });
});
